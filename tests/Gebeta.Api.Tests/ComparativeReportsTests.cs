using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>
/// Profit and loss and the balance sheet with a comparison column (docs/requirements/competitive-features.md #3), on the requirement's
/// worked example moved one month earlier: Tikimt 2019 (11 Oct – 9 Nov 2026) against Tikimt 2018 (11 Oct – 9 Nov 2025).
/// </summary>
public class ComparativeReportsTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task The_comparison_column_is_the_plain_report_for_its_dates_with_changes_by_account()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Comparative PLC", Ct);

        async Task Post(string date, string account, decimal amount, bool income)
        {
            var journal = await (await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
            {
                companyId = company.Id, postingDate = date, remark = "Test",
                lines = new[]
                {
                    new { accountCode = income ? "1120" : account, debit = amount, credit = 0m, description = (string?)null },
                    new { accountCode = income ? account : "1120", debit = 0m, credit = amount, description = (string?)null },
                },
            }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
            (await admin.PostAsync(U($"/api/accounting/journals/{journal!.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
        }

        // Tikimt 2018: sales 51.8m, cost of sales 36.9m, salaries 8.76m. Tikimt 2019: 62.4m, 41.3m, 9.85m and 42,500 bank charges.
        await Post("2025-10-20", "4100", 51_800_000m, income: true);
        await Post("2025-10-25", "5000", 36_900_000m, income: false);
        await Post("2025-11-05", "6110", 8_760_000m, income: false);
        await Post("2026-10-20", "4100", 62_400_000m, income: true);
        await Post("2026-10-25", "5000", 41_300_000m, income: false);
        await Post("2026-11-05", "6110", 9_850_000m, income: false);
        await Post("2026-11-06", "6400", 42_500m, income: false);

        var report = (await admin.GetFromJsonAsync<ComparativeReportDto>(
            U($"/api/accounting/reports/profit-and-loss/compare?companyId={company.Id}&from=2026-10-11&to=2026-11-09&compare=LastYear"), Ct))!;
        Assert.Equal((new DateOnly(2025, 10, 11), new DateOnly(2025, 11, 9)), (report.ComparisonFrom, report.ComparisonTo));
        ComparativeRow Row(string section, string code) => report.Sections.Single(s => s.Name == section).Rows.Single(r => r.Code == code);

        Assert.Equal(new ComparativeRow("4100", Row("Income", "4100").Name, 62_400_000m, 51_800_000m, 10_600_000m, 20.5m), Row("Income", "4100"));
        Assert.Equal((41_300_000m, 36_900_000m, 11.9m), (Row("Expenses", "5000").Current, Row("Expenses", "5000").Comparison, Row("Expenses", "5000").ChangePercent));
        Assert.Equal((1_090_000m, 12.4m), (Row("Expenses", "6110").Change, Row("Expenses", "6110").ChangePercent));
        Assert.Equal((42_500m, (decimal?)null, (decimal?)null), (Row("Expenses", "6400").Current, Row("Expenses", "6400").Comparison, Row("Expenses", "6400").ChangePercent)); // "–" and "n/a"
        var profit = Assert.Single(report.Summary);
        Assert.Equal((11_207_500m, 6_140_000m, 5_067_500m, 82.5m), (profit.Current!.Value, profit.Comparison!.Value, profit.Change, profit.ChangePercent!.Value));
        Assert.Null(report.Note);

        // The comparison column is exactly the plain report for its dates.
        var plain = (await admin.GetFromJsonAsync<ProfitAndLossDto>(U($"/api/accounting/reports/profit-and-loss?companyId={company.Id}&from=2025-10-11&to=2025-11-09"), Ct))!;
        Assert.Equal(plain.NetProfit, profit.Comparison);
        Assert.Equal(plain.Expenses.Select(r => (r.Code, (decimal?)r.Amount)), report.Sections.Single(s => s.Name == "Expenses").Rows.Where(r => r.Comparison != null).Select(r => (r.Code, r.Comparison)));

        // The balance sheet: end of Tikimt 2019 against end of Tikimt 2018, each balanced on its own and equal to the plain report.
        var sheet = (await admin.GetFromJsonAsync<ComparativeReportDto>(
            U($"/api/accounting/reports/balance-sheet/compare?companyId={company.Id}&asOf=2026-11-09&compare=LastYear"), Ct))!;
        Assert.True(sheet.CurrentBalanced && sheet.ComparisonBalanced);
        var plainSheet = (await admin.GetFromJsonAsync<BalanceSheetDto>(U($"/api/accounting/reports/balance-sheet?companyId={company.Id}&asOf=2025-11-09"), Ct))!;
        Assert.Equal(plainSheet.TotalAssets, sheet.Sections.Single(s => s.Name == "Assets").Total.Comparison);

        // Nothing posted in the comparison period: a note instead of a column of zeros.
        var early = (await admin.GetFromJsonAsync<ComparativeReportDto>(
            U($"/api/accounting/reports/profit-and-loss/compare?companyId={company.Id}&from=2025-10-11&to=2025-11-09&compare=LastYear"), Ct))!;
        Assert.NotNull(early.Note);

        var csv = await (await admin.GetAsync(U($"/api/accounting/reports/profit-and-loss/compare?companyId={company.Id}&from=2026-10-11&to=2026-11-09&compare=LastYear&format=csv"), Ct))
            .Content.ReadAsStringAsync(Ct);
        Assert.Contains("2025-10-11 to 2025-11-09", csv, StringComparison.Ordinal);
        Assert.Contains("82.5", csv, StringComparison.Ordinal);
    }
}
