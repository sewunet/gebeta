using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Cut-over opening balances and the year-end closing to retained earnings.</summary>
public class YearEndTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Opening_balances_then_a_year_closes_to_retained_earnings()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Year End PLC", abbreviation = "YEP", legalForm = "PrivateLimitedCompany", tin = "0800000041" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;

        object Line(string code, decimal debit, decimal credit) => new { accountCode = code, debit, credit };

        // Cut-over on the last day of FY 2018 (Sene 30, 2018 = 2026-07-07).
        var unbalanced = await admin.PostAsJsonAsync(U("/api/accounting/opening-balances"), new
        {
            companyId = id, date = "2026-07-07", lines = new[] { Line("1120", 200_000m, 0m), Line("3100", 0m, 150_000m) },
        }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unbalanced.StatusCode);
        Assert.Contains("50000", await unbalanced.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);

        var opening = await (await admin.PostAsJsonAsync(U("/api/accounting/opening-balances"), new
        {
            companyId = id, date = "2026-07-07",
            lines = new[] { Line("1120", 200_000m, 0m), Line("1500", 300_000m, 0m), Line("2100", 0m, 50_000m), Line("3100", 0m, 400_000m), Line("3200", 0m, 50_000m) },
        }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        Assert.Equal("Opening", opening!.Source);
        Assert.Equal("Submitted", opening.Status);

        // FY 2019 (2026-07-08 … 2027-07-07): sales 80,000, rent 30,000.
        foreach (var (date, debit, credit, amount) in new[] { ("2026-11-20", "1120", "4100", 80_000m), ("2027-02-01", "6200", "1120", 30_000m) })
        {
            var j = await (await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
            {
                companyId = id, postingDate = date, remark = "FY 2019", lines = new[] { Line(debit, amount, 0m), Line(credit, 0m, amount) },
            }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
            await admin.PostAsync(U($"/api/accounting/journals/{j!.Id}/submit"), null, Ct);
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/accounting/years/close"), new { companyId = id, date = "2026-07-01" }, Ct)).StatusCode);

        var closing = await (await admin.PostAsJsonAsync(U("/api/accounting/years/close"), new { companyId = id, date = "2027-03-01" }, Ct))
            .Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        Assert.Equal("YearEnd", closing!.Source);
        Assert.Equal(new DateOnly(2027, 7, 7), closing.PostingDate);
        Assert.Contains(closing.Lines, l => l.AccountCode == "4100" && l.Debit == 80_000m);
        Assert.Contains(closing.Lines, l => l.AccountCode == "6200" && l.Credit == 30_000m);
        Assert.Contains(closing.Lines, l => l.AccountCode == "3200" && l.Credit == 50_000m);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U("/api/accounting/years/close"), new { companyId = id, date = "2027-01-01" }, Ct)).StatusCode);

        // Profit and loss still shows the year's result; the balance sheet now has it in retained earnings.
        var pl = await admin.GetFromJsonAsync<ProfitAndLossDto>(U($"/api/accounting/reports/profit-and-loss?companyId={id}&from=2026-07-08&to=2027-07-07"), Ct);
        Assert.Equal(50_000m, pl!.NetProfit);
        var bs = await admin.GetFromJsonAsync<BalanceSheetDto>(U($"/api/accounting/reports/balance-sheet?companyId={id}&asOf=2027-07-07"), Ct);
        Assert.Equal(0m, bs!.UnclosedProfit);
        Assert.Equal(100_000m, bs.Equity.Single(r => r.Code == "3200").Amount);
        Assert.Equal(550_000m, bs.TotalAssets);
        Assert.True(bs.Balanced);

        // Cancelling the closing journal reopens the year.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/accounting/journals/{closing.Id}/cancel"), null, Ct)).StatusCode);
        bs = await admin.GetFromJsonAsync<BalanceSheetDto>(U($"/api/accounting/reports/balance-sheet?companyId={id}&asOf=2027-07-07"), Ct);
        Assert.Equal(50_000m, bs!.UnclosedProfit);
        Assert.True(bs.Balanced);

        // Close it again, then close Sene 2019: the year-end journal is now locked.
        closing = await (await admin.PostAsJsonAsync(U("/api/accounting/years/close"), new { companyId = id, date = "2027-03-01" }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(U("/api/accounting/periods/close"), new { companyId = id, date = "2027-07-01" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await admin.PostAsync(U($"/api/accounting/journals/{closing!.Id}/cancel"), null, Ct)).StatusCode);
    }
}
