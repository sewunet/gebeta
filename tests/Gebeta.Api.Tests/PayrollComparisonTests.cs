using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>
/// The payroll comparison with last month, before submit (docs/requirements/competitive-features.md #1), on the requirement's worked
/// example. Two changes: the months are the two before (Hidar ends in the future), and E-102's cut from 12,000 to 6,000 comes from a
/// new salary assignment instead of unpaid leave; the figures are the same.
/// </summary>
public class PayrollComparisonTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_run_shows_who_is_paid_differently_from_last_month_and_why_with_totals_that_match_the_reports()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Comparison PLC", Ct);
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Standard" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);
        const string lastStart = "2026-09-11", lastEnd = "2026-10-10", thisStart = "2026-10-11", thisEnd = "2026-11-09";

        async Task<EmployeeDto> Hire(string first, decimal basic, string joined = "2024-01-01")
        {
            var e = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
                new { companyId = company.Id, firstName = first, fatherName = "Test", gender = "Male", dateOfJoining = joined }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
            await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = e!.Id, structureId = structure!.Id, @base = basic, fromDate = joined }, Ct);
            return e;
        }

        var e101 = await Hire("Abebe", 8_000m);
        var e102 = await Hire("Bekele", 12_000m);
        var e103 = await Hire("Chala", 8_000m);
        var e105 = await Hire("Dereje", 8_000m);
        var e106 = await Hire("Elias", 3_500m);

        // Last month, paid and submitted.
        foreach (var e in new[] { e101, e102, e103, e105, e106 })
        {
            var slip = (await (await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = e.Id, periodStart = lastStart, periodEnd = lastEnd }, Ct))
                .Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/payroll/salary-slips/{slip.Id}/submit"), null, Ct)).StatusCode);
        }

        // This month: E-102 cut to 6,000; E-104 joins; E-105 left; overtime for E-101 (1,200) and E-103 (12,000, a typing error for 1,200).
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = e102.Id, structureId = structure!.Id, @base = 6_000m, fromDate = thisStart }, Ct);
        var e104 = await Hire("Fikru", 5_000m, joined: thisStart);
        await admin.PostAsJsonAsync(U($"/api/core/employees/{e105.Id}/leaving"), new { dateOfLeaving = lastEnd }, Ct);
        var overtime = (await (await admin.PostAsJsonAsync(U("/api/payroll/additional-salaries"), new
        {
            companyId = company.Id, payrollDate = "2026-10-20", component = "Overtime", abbreviation = "OT", type = "Earning", isTaxable = true,
            lines = new[] { new { employeeId = e101.Id, amount = 1_200m, note = (string?)null }, new { employeeId = e103.Id, amount = 12_000m, note = (string?)null } },
            remark = (string?)null,
        }, Ct)).Content.ReadFromJsonAsync<AdditionalSalaryDto>(Ct))!;
        await admin.PostAsync(U($"/api/payroll/additional-salaries/{overtime.Id}/submit"), null, Ct);

        var run = (await (await admin.PostAsJsonAsync(U("/api/payroll/runs"), new { companyId = company.Id, periodStart = thisStart, periodEnd = thisEnd }, Ct))
            .Content.ReadFromJsonAsync<PayrollRunDto>(Ct))!;
        Assert.True((await admin.PostAsync(U($"/api/payroll/runs/{run.Id}/generate"), null, Ct)).IsSuccessStatusCode);

        var comparison = (await admin.GetFromJsonAsync<PayrollComparisonDto>(U($"/api/payroll/runs/{run.Id}/comparison"), Ct))!;
        Assert.True(comparison.HasPrevious);
        PayComparisonRow Row(EmployeeDto e) => comparison.Rows.Single(r => r.EmployeeId == e.Id);

        // E-101: 9,200 gross; tax 9,200 × 25% − 850 = 1,450; pension 560; net 7,190 against 6,290: +900, +14.3%.
        Assert.Equal(("Changed", true, 900m, 14.3m), (Row(e101).Status, Row(e101).Flagged, Row(e101).NetChange, Row(e101).ChangePercent));
        Assert.Equal(new PayFigures(9_200m, 1_450m, 560m, 880m, 7_190m, 30m), Row(e101).Current);
        Assert.Equal($"Overtime {overtime.Number} +1,200.00", Assert.Single(Row(e101).Reasons));
        // E-102: 6,000; tax 6,000 × 20% − 500 = 700; pension 420; net 4,880 against 8,910: −4,030, −45.2%.
        Assert.Equal((-4_030m, -45.2m, true), (Row(e102).NetChange, Row(e102).ChangePercent, Row(e102).Flagged));
        Assert.Contains("new salary assignment this month", Row(e102).Reasons);
        // E-103: the typing error stands out at +130.4%; tax 20,000 × 35% − 2,050 = 4,950.
        Assert.Equal((8_200m, 130.4m, 4_950m), (Row(e103).NetChange, Row(e103).ChangePercent, Row(e103).Current!.IncomeTax));
        Assert.Equal(("New", true, 4_150m), (Row(e104).Status, Row(e104).Flagged, Row(e104).Current!.Net));
        Assert.Equal(("Missing", true, -6_290m), (Row(e105).Status, Row(e105).Flagged, Row(e105).NetChange));
        Assert.Contains($"left on {lastEnd}", Row(e105).Reasons);
        Assert.Equal(("Same", false), (Row(e106).Status, Row(e106).Flagged));
        Assert.Equal(5, comparison.FlaggedCount);

        Assert.Equal(new PayTotals(5, 39_500m, 5_925m, 2_765m, 4_345m, 30_810m), comparison.PreviousTotals);
        Assert.Equal(new PayTotals(5, 43_700m, 7_825m, 2_135m, 3_355m, 33_740m), comparison.CurrentTotals);

        // Last month's totals are the statutory reports' totals: one set of rules for both.
        var tax = (await admin.GetFromJsonAsync<IncomeTaxDeclarationDto>(U($"/api/payroll/reports/income-tax?companyId={company.Id}&periodStart={lastStart}&periodEnd={lastEnd}"), Ct))!;
        var pension = (await admin.GetFromJsonAsync<PensionScheduleDto>(U($"/api/payroll/reports/pension?companyId={company.Id}&periodStart={lastStart}&periodEnd={lastEnd}"), Ct))!;
        Assert.Equal((tax.TotalTaxWithheld, pension.TotalEmployee, pension.TotalEmployer),
            (comparison.PreviousTotals.IncomeTax, comparison.PreviousTotals.EmployeePension, comparison.PreviousTotals.EmployerPension));

        var csv = await admin.GetAsync(U($"/api/payroll/runs/{run.Id}/comparison?format=csv"), Ct);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        var text = await csv.Content.ReadAsStringAsync(Ct);
        Assert.Contains("Net Change", text, StringComparison.Ordinal);
        Assert.Contains("130.4", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_first_month_has_nothing_to_compare_and_flags_nobody()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("First Month PLC", Ct);
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Standard" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);
        var e = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Genet", fatherName = "Test", gender = "Female", dateOfJoining = "2024-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = e!.Id, structureId = structure!.Id, @base = 9_000m, fromDate = "2024-01-01" }, Ct);
        var run = (await (await admin.PostAsJsonAsync(U("/api/payroll/runs"), new { companyId = company.Id, periodStart = "2026-10-11", periodEnd = "2026-11-09" }, Ct))
            .Content.ReadFromJsonAsync<PayrollRunDto>(Ct))!;
        await admin.PostAsync(U($"/api/payroll/runs/{run.Id}/generate"), null, Ct);

        var comparison = (await admin.GetFromJsonAsync<PayrollComparisonDto>(U($"/api/payroll/runs/{run.Id}/comparison"), Ct))!;
        Assert.False(comparison.HasPrevious);
        Assert.Equal(0, comparison.FlaggedCount);
        Assert.Equal(1, comparison.CurrentTotals.Headcount);
    }
}
