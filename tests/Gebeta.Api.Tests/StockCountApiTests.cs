using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Stock.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Stock counts: opening stock, a count short of the books, and a backdated receipt that changes the count's difference.</summary>
public class StockCountApiTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task A_count_sets_the_stock_and_keeps_it_when_an_earlier_movement_is_entered_later()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Count PLC", abbreviation = "CNT", legalForm = "PrivateLimitedCompany", tin = "0800000151" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        var teff = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "TEFF", name = "Teff", uom = "quintal", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var main = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "MAIN", name = "Main" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        async Task<StockEntryDto> Post(object body)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/stock/entries"), body, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct);
            var response = await admin.PostAsync(U($"/api/stock/entries/{draft!.Id}/submit"), null, Ct);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        }

        async Task<StockBalanceRow> Balance(string asOf) =>
            (await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={id}&asOf={asOf}"), Ct))!.Rows.Single();

        // Opening stock: 20 quintals at 6,000 against capital.
        var opening = await Post(new
        {
            companyId = id, purpose = "Reconciliation", postingDate = "2026-11-01", counterAccountCode = "3100",
            lines = new[] { new { itemId = teff!.Id, qty = 20m, rate = 6_000m, targetWarehouseId = main!.Id } },
        });
        Assert.Equal(("3100", 120_000m), (opening.CounterAccountCode, opening.TotalValue));

        await Post(new { companyId = id, purpose = "Issue", postingDate = "2026-11-05", lines = new[] { new { itemId = teff.Id, qty = 8m, sourceWarehouseId = main.Id } } });

        // Count on the 12th: 10 found where the books say 12 → 2 short, written off at 6,000.
        var count = await Post(new
        {
            companyId = id, purpose = "Reconciliation", postingDate = "2026-11-12",
            lines = new[] { new { itemId = teff.Id, qty = 10m, rate = (decimal?)null, targetWarehouseId = main.Id } },
        });
        Assert.Equal(("5100", -12_000m), (count.CounterAccountCode, count.TotalValue));
        Assert.Equal((10m, 60_000m), ((await Balance("2026-11-30")).Qty, (await Balance("2026-11-30")).Value));

        // A receipt of 3 on the 8th is entered afterwards: the count still says 10, so it is now 5 short.
        await Post(new { companyId = id, purpose = "Receipt", postingDate = "2026-11-08", lines = new[] { new { itemId = teff.Id, qty = 3m, rate = 6_000m, targetWarehouseId = main.Id } } });
        var recounted = await admin.GetFromJsonAsync<StockEntryDto>(U($"/api/stock/entries/{count.Id}"), Ct);
        Assert.Equal(-30_000m, recounted!.TotalValue);
        var end = await Balance("2026-11-30");
        Assert.Equal((10m, 60_000m), (end.Qty, end.Value));
        Assert.Equal(15m, (await Balance("2026-11-08")).Qty); // 20 − 8 + 3 before the count

        // Cancelling the count gives the books back their own figure.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/stock/entries/{count.Id}/cancel"), null, Ct)).StatusCode);
        Assert.Equal(15m, (await Balance("2026-11-30")).Qty);

        // Refused: a supplier on a count.
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "A Supplier", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/stock/entries"), new
        {
            companyId = id, purpose = "Reconciliation", postingDate = "2026-11-12", partyId = supplier!.Id,
            lines = new[] { new { itemId = teff.Id, qty = 1m, rate = 1m, targetWarehouseId = main.Id } },
        }, Ct)).StatusCode);
    }
}
