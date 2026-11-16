using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>
/// Accounting next to Payroll. In these tests RabbitMQ is off, so the event consumers are called directly with
/// the same events Payroll publishes; delivery over RabbitMQ is tested in Gebeta.Messaging.Tests and live.
/// </summary>
public class AccountingTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private async Task Deliver<TConsumer, TEvent>(TEvent integrationEvent)
        where TConsumer : IIntegrationEventConsumer<TEvent>
        where TEvent : class
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IAccountingUnitOfWork>();
        await using var transaction = await unitOfWork.BeginTransactionAsync(Ct); // as the RabbitMQ consumer does
        await scope.ServiceProvider.GetRequiredService<TConsumer>().HandleAsync(integrationEvent, Ct);
        await transaction.CommitAsync(Ct);
    }

    [Fact]
    public async Task A_submitted_salary_slip_becomes_a_balanced_payroll_journal_and_cancelling_reverses_it()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Books Test PLC", abbreviation = "BKS", legalForm = "PrivateLimitedCompany", tin = "0800000001" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);

        // The chart comes from the CompanyCreated event.
        await Deliver<CompanyCreatedConsumer, CompanyCreated>(new CompanyCreated(company!.Id, company.Name, company.Abbreviation, "ETB", DateTimeOffset.UtcNow));
        var accounts = await admin.GetFromJsonAsync<List<AccountDto>>(U($"/api/accounting/accounts?companyId={company.Id}"), Ct);
        Assert.Contains(accounts!, a => a.Code == "2220" && a.Name == "Employment Income Tax Payable");

        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Staff" }, Ct)).Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);
        var employee = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Tesfaye", gender = "Female", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = employee!.Id, structureId = structure!.Id, @base = 13_000m, fromDate = "2025-01-01" }, Ct);
        var slip = await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = employee.Id, periodStart = "2026-11-01", periodEnd = "2026-11-30" }, Ct))
            .Content.ReadFromJsonAsync<SalarySlipDto>(Ct);
        slip = await (await admin.PostAsync(U($"/api/payroll/salary-slips/{slip!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct);

        var submitted = new SalarySlipSubmitted(slip!.Id, slip.Number!, company.Id, employee.Id, slip.PeriodStart, slip.PeriodEnd, slip.GrossPay, slip.TotalDeductions,
            slip.NetPay, slip.EmployerContributions, slip.Lines.Select(l => new SalarySlipLineData(l.Abbreviation, l.Component, l.Type, l.Amount)).ToList(), DateTimeOffset.UtcNow);
        await Deliver<SalarySlipSubmittedConsumer, SalarySlipSubmitted>(submitted);
        await Deliver<SalarySlipSubmittedConsumer, SalarySlipSubmitted>(submitted); // a repeated event does nothing

        var journals = await admin.GetFromJsonAsync<List<JournalEntryDto>>(U($"/api/accounting/journals?companyId={company.Id}&from=2026-11-01&to=2026-11-30"), Ct);
        var journal = Assert.Single(journals!);
        Assert.Equal("Payroll", journal.Source);
        Assert.Equal(slip.Number, journal.SourceNumber);
        Assert.Equal("JV-2019-00001", journal.Number);

        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-11-30"), Ct);
        decimal Debit(string code) => tb!.Rows.Single(r => r.Code == code).Debit;
        decimal Credit(string code) => tb!.Rows.Single(r => r.Code == code).Credit;
        Assert.Equal(13_000m, Debit("6110"));          // salaries and wages
        Assert.Equal(1_430m, Debit("6120"));           // employer pension 11%
        Assert.Equal(2_550m, Credit("2220"));          // employment income tax payable
        Assert.Equal(910m + 1_430m, Credit("2230"));   // pension payable 7% + 11%
        Assert.Equal(9_540m, Credit("2210"));          // salaries payable (net)
        Assert.Equal(tb!.TotalDebit, tb.TotalCredit);

        // A payroll journal is cancelled from Payroll, not by hand.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/accounting/journals/{journal.Id}/cancel"), null, Ct)).StatusCode);

        await Deliver<SalarySlipCancelledConsumer, SalarySlipCancelled>(new SalarySlipCancelled(slip.Id, slip.Number!, company.Id, DateTimeOffset.UtcNow));
        var after = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-11-30"), Ct);
        Assert.Empty(after!.Rows);
    }

    [Fact]
    public async Task Manual_journals_must_balance_and_post_to_leaf_accounts()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Manual Books PLC", abbreviation = "MBK", legalForm = "PrivateLimitedCompany", tin = "0800000002" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        await admin.PostAsync(U($"/api/accounting/companies/{company!.Id}/chart"), null, Ct);

        var group = await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
        {
            companyId = company.Id, postingDate = "2026-11-30", remark = "Wrong",
            lines = new object[] { new { accountCode = "6000", debit = 100, credit = 0 }, new { accountCode = "1110", debit = 0, credit = 100 } },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, group.StatusCode);

        var unbalanced = await (await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
        {
            companyId = company.Id, postingDate = "2026-11-30", remark = "Rent",
            lines = new object[] { new { accountCode = "6200", debit = 5000, credit = 0 }, new { accountCode = "1120", debit = 0, credit = 4000 } },
        }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/accounting/journals/{unbalanced!.Id}/submit"), null, Ct)).StatusCode);

        var rent = await (await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
        {
            companyId = company.Id, postingDate = "2026-11-30", remark = "Office rent, Hidar 2019",
            lines = new object[] { new { accountCode = "6200", debit = 5000, credit = 0 }, new { accountCode = "1120", debit = 0, credit = 5000 } },
        }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        var posted = await (await admin.PostAsync(U($"/api/accounting/journals/{rent!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        Assert.Equal("Submitted", posted!.Status);

        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-11-30"), Ct);
        Assert.Equal(5_000m, tb!.Rows.Single(r => r.Code == "6200").Balance);
        Assert.Equal(-5_000m, tb.Rows.Single(r => r.Code == "1120").Balance);
    }
}
