using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Cost centres (docs/requirements/sap-features.md #3): the master, journals that name them, payroll cost by employee, and P&amp;L per cost centre.</summary>
public class CostCenterTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Profit_and_loss_splits_by_cost_centre_and_postings_that_name_none_belong_to_main()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Cost Centre PLC", Ct);
        var other = await admin.NewCompanyAsync("Other Cost Centre PLC", Ct);

        var main = Assert.Single((await admin.GetFromJsonAsync<List<CostCenterDto>>(U($"/api/core/cost-centers?companyId={company.Id}"), Ct))!);
        Assert.Equal(("MAIN", true), (main.Code, main.IsMain));
        async Task<CostCenterDto> Center(Guid companyId, string code, string name) =>
            (await (await admin.PostAsJsonAsync(U("/api/core/cost-centers"), new { companyId, code, name }, Ct)).Content.ReadFromJsonAsync<CostCenterDto>(Ct))!;
        var estate = await Center(company.Id, "estate", "Cane estate");
        var factoryCentre = await Center(company.Id, "FACTORY", "Factory");
        var foreign = await Center(other.Id, "ESTATE", "Elsewhere");
        Assert.Equal("ESTATE", estate.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U("/api/core/cost-centers"), new { companyId = company.Id, code = "ESTATE", name = "Again" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(U($"/api/core/cost-centers/{main.Id}"), new { name = "Main", isActive = false }, Ct)).StatusCode);

        async Task<HttpResponseMessage> Journal(string account, decimal amount, bool income, Guid? centre)
        {
            var created = await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
            {
                companyId = company.Id, postingDate = "2026-11-12", remark = "Test",
                lines = new object[]
                {
                    new { accountCode = income ? "1120" : account, debit = amount, credit = 0m, description = (string?)null, costCenterId = income ? null : centre },
                    new { accountCode = income ? account : "1120", debit = 0m, credit = amount, description = (string?)null, costCenterId = income ? centre : null },
                },
            }, Ct);
            if (created.IsSuccessStatusCode)
            {
                var journal = (await created.Content.ReadFromJsonAsync<JournalEntryDto>(Ct))!;
                (await admin.PostAsync(U($"/api/accounting/journals/{journal.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
            }

            return created;
        }

        (await Journal("4100", 1_000m, income: true, estate.Id)).EnsureSuccessStatusCode();
        (await Journal("6900", 300m, income: false, factoryCentre.Id)).EnsureSuccessStatusCode();
        (await Journal("6200", 200m, income: false, null)).EnsureSuccessStatusCode(); // names none: Main
        Assert.Equal(HttpStatusCode.BadRequest, (await Journal("6900", 50m, income: false, foreign.Id)).StatusCode);

        // Payroll: the slip's salary cost goes to the employee's cost centre, the payables to none.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAccountingUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<SalarySlipSubmittedConsumer>().HandleAsync(new SalarySlipSubmitted(Guid.NewGuid(), "SAL-T-1", company.Id,
                Guid.NewGuid(), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 5_000m, 350m, 4_650m, 550m,
                [new SalarySlipLineData("BS", "Basic Salary", "Earning", 5_000m), new SalarySlipLineData("PE", "Pension", "Deduction", 350m),
                 new SalarySlipLineData("PER", "Employer Pension", "EmployerContribution", 550m)], DateTimeOffset.UtcNow, factoryCentre.Id), Ct);
            await transaction.CommitAsync(Ct);
        }

        async Task<ProfitAndLossDto> Pl(Guid? centre) => (await admin.GetFromJsonAsync<ProfitAndLossDto>(
            U($"/api/accounting/reports/profit-and-loss?companyId={company.Id}&from=2026-11-01&to=2026-11-30{(centre is null ? "" : $"&costCenterId={centre}")}"), Ct))!;
        var all = await Pl(null);
        var byEstate = await Pl(estate.Id);
        var byFactory = await Pl(factoryCentre.Id);
        var byMain = await Pl(main.Id);
        Assert.Equal((1_000m, 0m), (byEstate.TotalIncome, byEstate.TotalExpenses));
        Assert.Equal(300m + 5_000m + 550m, byFactory.TotalExpenses); // other expenses, salaries, employer pension
        Assert.Equal((0m, 200m), (byMain.TotalIncome, byMain.TotalExpenses));
        Assert.Equal(all.NetProfit, byEstate.NetProfit + byFactory.NetProfit + byMain.NetProfit); // the split adds up to the whole

        var compared = (await admin.GetFromJsonAsync<ComparativeReportDto>(
            U($"/api/accounting/reports/profit-and-loss/compare?companyId={company.Id}&from=2026-11-01&to=2026-11-30&compare=LastYear&costCenterId={estate.Id}"), Ct))!;
        Assert.Equal(1_000m, compared.Summary[0].Current);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync(U($"/api/accounting/reports/profit-and-loss?companyId={company.Id}&from=2026-11-01&to=2026-11-30&costCenterId={foreign.Id}"), Ct)).StatusCode);
    }

    [Fact]
    public async Task An_employee_gets_a_cost_centre_of_their_own_company_only()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Profile Centre PLC", Ct);
        var other = await admin.NewCompanyAsync("Profile Elsewhere PLC", Ct);
        var employee = (await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Almaz", fatherName = "Test", gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct))!;
        var mine = (await (await admin.PostAsJsonAsync(U("/api/core/cost-centers"), new { companyId = company.Id, code = "WORKSHOP", name = "Workshop" }, Ct))
            .Content.ReadFromJsonAsync<CostCenterDto>(Ct))!;
        var theirs = Assert.Single((await admin.GetFromJsonAsync<List<CostCenterDto>>(U($"/api/core/cost-centers?companyId={other.Id}"), Ct))!);

        object Profile(Guid centre) => new { paymentMethod = "Cash", bankName = (string?)null, bankAccountNumber = (string?)null, pensionNumber = "P-9", costCenterId = centre };
        var saved = (await (await admin.PutAsJsonAsync(U($"/api/payroll/employees/{employee.Id}/profile"), Profile(mine.Id), Ct)).Content.ReadFromJsonAsync<PayrollProfileDto>(Ct))!;
        Assert.Equal(mine.Id, saved.CostCenterId);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(U($"/api/payroll/employees/{employee.Id}/profile"), Profile(theirs.Id), Ct)).StatusCode);
    }
}
