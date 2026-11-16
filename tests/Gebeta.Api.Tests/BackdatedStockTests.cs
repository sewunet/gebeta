using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Backdated entries and cancelling older entries reprice later movements, through transfers too.</summary>
public class BackdatedStockTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_backdated_receipt_reprices_later_issues_and_transfers_and_cancelling_it_puts_them_back()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Backdate PLC", abbreviation = "BKD", legalForm = "PrivateLimitedCompany", tin = "0800000111" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        var item = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "TEFF", name = "Teff", uom = "quintal", isStockItem = true }, Ct))
            .Content.ReadFromJsonAsync<ItemDto>(Ct);
        var main = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "MAIN", name = "Main" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);
        var shop = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "SHOP", name = "Shop", stockAccountCode = "1300" }, Ct))
            .Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        async Task<HttpResponseMessage> Post(string purpose, string date, decimal qty, decimal? rate, Guid? from, Guid? to)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/stock/entries"), new
            {
                companyId = id, purpose, postingDate = date,
                lines = new[] { new { itemId = item!.Id, qty, rate, sourceWarehouseId = from, targetWarehouseId = to } },
            }, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct);
            return await admin.PostAsync(U($"/api/stock/entries/{draft!.Id}/submit"), null, Ct);
        }

        async Task<StockEntryDto> Get(Guid entryId) => (await admin.GetFromJsonAsync<StockEntryDto>(U($"/api/stock/entries/{entryId}"), Ct))!;

        // Moving average. Receipt 10 @ 6,000 on the 12th; transfer 10 to the shop on the 13th; issue 4 from the shop on the 15th.
        await Post("Receipt", "2026-11-12", 10m, 6_000m, null, main!.Id);
        var transfer = (await (await Post("Transfer", "2026-11-13", 10m, null, main.Id, shop!.Id)).Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        var issue = (await (await Post("Issue", "2026-11-15", 4m, null, shop.Id, null)).Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        Assert.Equal(60_000m, transfer.TotalValue);
        Assert.Equal(24_000m, issue.TotalValue);

        // Backdated: 10 more @ 5,000 into MAIN on the 10th. MAIN's average on the 13th becomes 5,500, so the transfer is
        // worth 55,000, the shop's stock is worth 5,500 each, and the issue is repriced to 22,000.
        var backdated = await Post("Receipt", "2026-11-10", 10m, 5_000m, null, main.Id);
        Assert.Equal(HttpStatusCode.OK, backdated.StatusCode);
        var early = (await backdated.Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        Assert.Equal(55_000m, (await Get(transfer.Id)).TotalValue);
        Assert.Equal(22_000m, (await Get(issue.Id)).TotalValue);

        var balance = await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={id}&asOf=2026-11-30"), Ct);
        Assert.Equal((10m, 55_000m), balance!.Rows.Where(r => r.Warehouse == "MAIN").Select(r => (r.Qty, r.Value)).Single());
        Assert.Equal((6m, 33_000m), balance.Rows.Where(r => r.Warehouse == "SHOP").Select(r => (r.Qty, r.Value)).Single());
        Assert.Equal(110_000m - 22_000m, balance.TotalValue); // everything received less what was issued

        // On the 11th only the backdated stock existed.
        var onEleventh = await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={id}&asOf=2026-11-11"), Ct);
        Assert.Equal(50_000m, onEleventh!.TotalValue);

        // A backdated issue that would leave a later movement short is refused, and nothing changes.
        var tooEarly = await Post("Issue", "2026-11-12", 15m, null, main.Id, null); // 20 there that day, but the transfer on the 13th then lacks 5
        Assert.Equal(HttpStatusCode.BadRequest, tooEarly.StatusCode);
        Assert.Contains("below zero", await tooEarly.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(22_000m, (await Get(issue.Id)).TotalValue);

        // Cancelling the backdated receipt (no longer the latest movement) puts the old values back.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/stock/entries/{early.Id}/cancel"), null, Ct)).StatusCode);
        Assert.Equal(60_000m, (await Get(transfer.Id)).TotalValue);
        Assert.Equal(24_000m, (await Get(issue.Id)).TotalValue);
        var after = await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={id}&asOf=2026-11-30"), Ct);
        Assert.Equal(36_000m, after!.TotalValue);

        // Adjustment journals waiting in the outbox for Accounting: the issue repriced (twice) and the cancelled receipt.
        // The transfer moved value between two warehouses on the same stock account, so it needs none.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();
        var adjustments = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM stock.outbox_message WHERE clr_type LIKE '%StockJournalAdjusted%' AND payload::text LIKE {"%" + id + "%"}")
            .SingleAsync(Ct);
        Assert.Equal(3, adjustments);
    }
}
