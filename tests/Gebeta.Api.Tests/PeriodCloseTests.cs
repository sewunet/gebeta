using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Month-end close on the Ethiopian calendar, the lock date, and the financial reports.</summary>
public class PeriodCloseTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_month_closes_with_its_reports_and_then_nothing_posts_into_it_until_reopened()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Month Close PLC", abbreviation = "MCL", legalForm = "PrivateLimitedCompany", tin = "0800000031" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        await admin.PostAsync(U($"/api/accounting/companies/{id}/chart"), null, Ct);

        async Task<JournalEntryDto> Journal(string date, string debit, string credit, decimal amount, bool submit = true)
        {
            var j = await (await admin.PostAsJsonAsync(U("/api/accounting/journals"), new
            {
                companyId = id, postingDate = date, remark = $"{debit}/{credit}",
                lines = new object[] { new { accountCode = debit, debit = amount, credit = 0 }, new { accountCode = credit, debit = 0, credit = amount } },
            }, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct);
            return submit ? (await (await admin.PostAsync(U($"/api/accounting/journals/{j!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<JournalEntryDto>(Ct))! : j!;
        }

        // Hidar 2019 = 2026-11-10 … 2026-12-09.
        await Journal("2026-11-12", "1120", "3100", 100_000m); // capital
        await Journal("2026-11-20", "1120", "4100", 30_000m);  // sales
        await Journal("2026-11-25", "6200", "1120", 8_000m);   // rent
        var draft = await Journal("2026-11-28", "6300", "1120", 2_000m, submit: false);

        var blocked = await admin.PostAsJsonAsync(U("/api/accounting/periods/close"), new { companyId = id, date = "2026-11-15" }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        Assert.Contains(draft.Number!, await blocked.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await admin.PostAsync(U($"/api/accounting/journals/{draft.Id}/submit"), null, Ct);

        var pl = await admin.GetFromJsonAsync<ProfitAndLossDto>(U($"/api/accounting/reports/profit-and-loss?companyId={id}&from=2026-11-10&to=2026-12-09"), Ct);
        Assert.Equal(30_000m, pl!.TotalIncome);
        Assert.Equal(10_000m, pl.TotalExpenses);
        Assert.Equal(20_000m, pl.NetProfit);

        var bs = await admin.GetFromJsonAsync<BalanceSheetDto>(U($"/api/accounting/reports/balance-sheet?companyId={id}&asOf=2026-12-09"), Ct);
        Assert.Equal(120_000m, bs!.TotalAssets);
        Assert.Equal(20_000m, bs.UnclosedProfit);
        Assert.Equal(120_000m, bs.TotalEquity);
        Assert.True(bs.Balanced);

        var gl = await admin.GetFromJsonAsync<GeneralLedgerDto>(U($"/api/accounting/reports/general-ledger?companyId={id}&account=1120&from=2026-11-20&to=2026-12-09"), Ct);
        Assert.Equal(100_000m, gl!.Opening);
        Assert.Equal([130_000m, 122_000m, 120_000m], gl.Rows.Select(r => r.Balance));
        Assert.Equal(120_000m, gl.Closing);

        // Close Hidar.
        var closed = await (await admin.PostAsJsonAsync(U("/api/accounting/periods/close"), new { companyId = id, date = "2026-11-15" }, Ct))
            .Content.ReadFromJsonAsync<PeriodClosingDto>(Ct);
        Assert.Equal("Hidar 2019", closed!.Label);
        Assert.Equal(new DateOnly(2026, 12, 9), closed.End);
        Assert.Equal(20_000m, closed.NetProfit);
        Assert.Equal(closed.TotalDebit, closed.TotalCredit);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U("/api/accounting/periods/close"), new { companyId = id, date = "2026-11-20" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/accounting/periods/close"), new { companyId = id, date = "2027-01-15" }, Ct)).StatusCode);

        // The lock date: a journal dated in Hidar cannot be posted; one in Tahsas can.
        var late = await Journal("2026-11-30", "6900", "1120", 500m, submit: false);
        var refused = await admin.PostAsync(U($"/api/accounting/journals/{late.Id}/submit"), null, Ct);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Contains("accounting.period_closed", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal("Submitted", (await Journal("2026-12-15", "6900", "1120", 500m)).Status);

        var periods = await admin.GetFromJsonAsync<PeriodClosingsDto>(U($"/api/accounting/periods?companyId={id}"), Ct);
        Assert.Equal(new DateOnly(2026, 12, 9), periods!.LockDate);

        // Reopen with a reason, then the late journal posts.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/accounting/periods/reopen"), new { companyId = id, reason = "" }, Ct)).StatusCode);
        var reopened = await (await admin.PostAsJsonAsync(U("/api/accounting/periods/reopen"), new { companyId = id, reason = "Late utility bill" }, Ct))
            .Content.ReadFromJsonAsync<PeriodClosingDto>(Ct);
        Assert.Equal("Reopened", reopened!.Status);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/accounting/journals/{late.Id}/submit"), null, Ct)).StatusCode);
        periods = await admin.GetFromJsonAsync<PeriodClosingsDto>(U($"/api/accounting/periods?companyId={id}"), Ct);
        Assert.Null(periods!.LockDate);
    }
}
