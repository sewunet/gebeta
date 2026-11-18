using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Employee loans and standing deductions taken by the month's salary slips, and the payroll journal they post.</summary>
public class EmployeeLoanFlowTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Each_slip_deducts_the_instalments_once_until_the_loan_is_repaid_and_the_journal_credits_employee_advances()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Loans PLC", Ct);
        var structure = (await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Staff" }, Ct)).Content.ReadFromJsonAsync<SalaryStructureDto>(Ct))!;
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Tesfaye", gender = "Female", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = employee.Id, structureId = structure.Id, @base = 10_000m, fromDate = "2025-01-01" }, Ct);

        async Task<EmployeeLoanDto> Record(object body)
        {
            var created = await admin.PostAsJsonAsync(U("/api/payroll/loans"), body, Ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var draft = (await created.Content.ReadFromJsonAsync<EmployeeLoanDto>(Ct))!;
            return (await (await admin.PostAsync(U($"/api/payroll/loans/{draft.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<EmployeeLoanDto>(Ct))!;
        }

        var loan = await Record(new { companyId = company.Id, employeeId = employee.Id, kind = "Loan", component = "Salary advance", principal = 2_500m, instalment = 1_000m, startMonth = "2026-09-15" });
        var idir = await Record(new { companyId = company.Id, employeeId = employee.Id, kind = "StandingDeduction", component = "Idir", abbreviation = "IDIR", instalment = 50m, startMonth = "2026-09-01" });
        Assert.Equal(("Running", 2_500m, "LOAN-2019-00001"), (loan.Status, loan.Balance!.Value, loan.Number));

        async Task<SalarySlipDto> Slip(string start, string end)
        {
            var response = await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = employee.Id, periodStart = start, periodEnd = end }, Ct);
            Assert.True(response.IsSuccessStatusCode, $"{start}: {await response.Content.ReadAsStringAsync(Ct)}");
            return (await response.Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
        }
        decimal Line(SalarySlipDto s, string abbreviation) => s.Lines.Where(l => l.Abbreviation == abbreviation).Sum(l => l.Amount);

        // September: both deducted after tax; tax and pension are as without them.
        var september = await Slip("2026-09-01", "2026-09-30");
        Assert.Equal((1_000m, 50m), (Line(september, "LOAN"), Line(september, "IDIR")));
        Assert.Contains(september.Lines, l => l.Component == $"Salary advance ({loan.Number})");
        Assert.Equal(september.GrossPay - Line(september, "IT") - Line(september, "PE") - 1_050m, september.NetPay);

        // A submitted slip cancelled and made again deducts the same instalment once.
        (await admin.PostAsync(U($"/api/payroll/salary-slips/{september.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
        (await admin.PostAsync(U($"/api/payroll/salary-slips/{september.Id}/cancel"), null, Ct)).EnsureSuccessStatusCode();
        september = await Slip("2026-09-01", "2026-09-30");
        Assert.Equal((1_000m, 50m), (Line(september, "LOAN"), Line(september, "IDIR")));
        Assert.Equal(1_500m, (await admin.GetFromJsonAsync<EmployeeLoanDto>(U($"/api/payroll/loans/{loan.Id}"), Ct))!.Balance);

        // Paused in October: only Idir. November and December repay the rest: 1,000, then the last 500.
        await admin.PostAsync(U($"/api/payroll/loans/{loan.Id}/pause"), null, Ct);
        var october = await Slip("2026-10-01", "2026-10-31");
        Assert.Equal((0m, 50m), (Line(october, "LOAN"), Line(october, "IDIR")));
        await admin.PostAsync(U($"/api/payroll/loans/{loan.Id}/resume"), null, Ct);
        Assert.Equal(1_000m, Line(await Slip("2026-11-01", "2026-11-30"), "LOAN"));
        var december = await Slip("2026-12-01", "2026-12-31");
        Assert.Equal(500m, Line(december, "LOAN"));

        loan = (await admin.GetFromJsonAsync<EmployeeLoanDto>(U($"/api/payroll/loans/{loan.Id}"), Ct))!;
        Assert.Equal(("Completed", 0m, 2_500m), (loan.Status, loan.Balance!.Value, loan.Recovered));
        Assert.Equal([1_000m, 1_000m, 500m], loan.Repayments.Select(r => r.Amount));
        Assert.All(loan.Repayments, r => Assert.True(r.InSlip));

        // A slip took an instalment: the loan can no longer be cancelled; Idir is stopped instead.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/payroll/loans/{idir.Id}/cancel"), null, Ct)).StatusCode);
        idir = (await (await admin.PostAsJsonAsync(U($"/api/payroll/loans/{idir.Id}/settle"), new { date = "2026-12-31", reference = "Left the Idir" }, Ct)).Content.ReadFromJsonAsync<EmployeeLoanDto>(Ct))!;
        Assert.Equal(("Completed", new DateOnly(2026, 12, 31)), (idir.Status, idir.EndMonth!.Value));
        Assert.Equal(0m, Line(await Slip("2027-01-01", "2027-01-31"), "IDIR"));

        var list = (await admin.GetFromJsonAsync<List<EmployeeLoanDto>>(U($"/api/payroll/loans?companyId={company.Id}&employeeId={employee.Id}"), Ct))!;
        Assert.Equal(2, list.Count);

        // The payroll journal: the loan instalment repays Employee Advances (1400); Idir is owed on (2240).
        december = (await (await admin.PostAsync(U($"/api/payroll/salary-slips/{december.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAccountingUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<SalarySlipSubmittedConsumer>().HandleAsync(new SalarySlipSubmitted(december.Id, december.Number!, company.Id,
                employee.Id, december.PeriodStart, december.PeriodEnd, december.GrossPay, december.TotalDeductions, december.NetPay, december.EmployerContributions,
                december.Lines.Select(l => new SalarySlipLineData(l.Abbreviation, l.Component, l.Type, l.Amount)).ToList(), DateTimeOffset.UtcNow), Ct);
            await transaction.CommitAsync(Ct);
        }

        var tb = (await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-12-31"), Ct))!;
        Assert.Equal(500m, tb.Rows.Single(r => r.Code == "1400").Credit);
        Assert.Equal(50m, tb.Rows.Single(r => r.Code == "2240").Credit);
    }

    [Fact]
    public async Task A_loan_for_another_companys_employee_is_refused()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Loans Check PLC", Ct);
        var other = await admin.NewCompanyAsync("Loans Elsewhere PLC", Ct);
        var stranger = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = other.Id, firstName = "Kebede", fatherName = "Alemu", gender = "Male", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        var refused = await admin.PostAsJsonAsync(U("/api/payroll/loans"),
            new { companyId = company.Id, employeeId = stranger.Id, kind = "Loan", component = "Advance", principal = 1_000m, instalment = 500m, startMonth = "2026-09-01" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }
}
