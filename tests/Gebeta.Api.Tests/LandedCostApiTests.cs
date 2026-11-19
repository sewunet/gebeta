using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Landed costs: freight and duty become part of the item cost, later issues are repriced, and cancelling puts everything back.</summary>
public class LandedCostApiTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task Charges_raise_the_cost_of_the_received_items_and_the_issues_after_them()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Landed", Ct);
        var id = company.Id;
        var teff = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "TEFF", name = "Teff", uom = "quintal", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var oil = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "OIL", name = "Oil", uom = "drum", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var main = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "MAIN", name = "Main" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        async Task<StockEntryDto> Post(object body)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/stock/entries"), body, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct);
            var response = await admin.PostAsync(U($"/api/stock/entries/{draft!.Id}/submit"), null, Ct);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(Ct));
            return (await response.Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        }

        async Task<StockBalanceRow> Balance(string item) =>
            (await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={id}&asOf=2026-11-30"), Ct))!.Rows.Single(r => r.ItemCode == item);

        // Received on the 10th: 10 teff at 100 = 1,000 and 5 oil at 400 = 2,000. Four teff go out on the 12th at 100 each.
        var receipt = await Post(new
        {
            companyId = id, purpose = "Receipt", postingDate = "2026-11-10",
            lines = new[] { new { itemId = teff!.Id, qty = 10m, rate = 100m, targetWarehouseId = main!.Id }, new { itemId = oil!.Id, qty = 5m, rate = 400m, targetWarehouseId = main.Id } },
        });
        var issue = await Post(new { companyId = id, purpose = "Issue", postingDate = "2026-11-12", lines = new[] { new { itemId = teff.Id, qty = 4m, sourceWarehouseId = main.Id } } });
        Assert.Equal(400m, issue.TotalValue);

        async Task<HttpResponseMessage> CreateCost(object body) => await admin.PostAsJsonAsync(U("/api/stock/landed-costs"), body, Ct);

        // Freight 300 by value: 1,000 : 2,000 → teff 100, oil 200. Duty 150 by quantity: 10 : 5 → teff 100, oil 50.
        var created = await CreateCost(new
        {
            receiptId = receipt.Id, postingDate = "2026-11-15", remark = "Djibouti shipment",
            charges = new object[]
            {
                new { description = "Freight", amount = 300m, basis = "Value" },
                new { description = "Customs duty", amount = 150m, basis = "Quantity", accountCode = "2160" },
            },
        });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<LandedCostDto>(Ct))!;
        Assert.Equal(("Draft", 450m), (draft.Status, draft.TotalCharges));
        Assert.Equal([200m, 250m], draft.Lines.Select(l => l.Allocated).ToArray());
        Assert.Equal(600m, (await Balance("TEFF")).Value); // a draft changes nothing: 1,000 received less 400 issued

        var submitted = await admin.PostAsync(U($"/api/stock/landed-costs/{draft.Id}/submit"), null, Ct);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        // Teff cost 1,200 for 10 = 120 each. The 4 issued are now worth 480, the 6 left 720. Oil 2,250 for 5 = 450 each.
        var teffBalance = await Balance("TEFF");
        Assert.Equal((6m, 120m, 720m), (teffBalance.Qty, teffBalance.Rate, teffBalance.Value));
        var oilBalance = await Balance("OIL");
        Assert.Equal((5m, 450m, 2_250m), (oilBalance.Qty, oilBalance.Rate, oilBalance.Value));
        Assert.Equal(480m, (await admin.GetFromJsonAsync<StockEntryDto>(U($"/api/stock/entries/{issue.Id}"), Ct))!.TotalValue);
        Assert.Equal(3_450m, (await admin.GetFromJsonAsync<StockEntryDto>(U($"/api/stock/entries/{receipt.Id}"), Ct))!.TotalValue);

        // The receipt cannot be cancelled while it carries landed costs; submitting twice is refused.
        var refused = await admin.PostAsync(U($"/api/stock/entries/{receipt.Id}/cancel"), null, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("landed costs", await refused.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/stock/landed-costs/{draft.Id}/submit"), null, Ct)).StatusCode);
        Assert.Single((await admin.GetFromJsonAsync<List<LandedCostDto>>(U($"/api/stock/landed-costs?receiptId={receipt.Id}"), Ct))!);

        // Cancelling takes the charges back out: teff 600 left at 100, the issue back to 400, oil 2,000.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/stock/landed-costs/{draft.Id}/cancel"), null, Ct)).StatusCode);
        Assert.Equal((6m, 100m, 600m), ((await Balance("TEFF")).Qty, (await Balance("TEFF")).Rate, (await Balance("TEFF")).Value));
        Assert.Equal(2_000m, (await Balance("OIL")).Value);
        Assert.Equal(400m, (await admin.GetFromJsonAsync<StockEntryDto>(U($"/api/stock/entries/{issue.Id}"), Ct))!.TotalValue);
        Assert.Equal(3_000m, (await admin.GetFromJsonAsync<StockEntryDto>(U($"/api/stock/entries/{receipt.Id}"), Ct))!.TotalValue);

        // Journals waiting for Accounting: the charges (Dr 1300 450 / Cr 2150 300, Cr 2160 150), the issue repriced, and both undone.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<StockDbContext>();
        var payloads = await db.Database
            .SqlQuery<string>($"SELECT payload::text AS \"Value\" FROM stock.outbox_message WHERE clr_type LIKE '%StockJournalAdjusted%' AND payload::text LIKE {"%" + id + "%"}")
            .ToListAsync(Ct);
        Assert.Equal(4, payloads.Count);
        var charges = payloads.Single(p => p.Contains("Landed cost LCV-", StringComparison.Ordinal) && !p.Contains("Cancelled", StringComparison.Ordinal));
        Assert.Contains("\"2160\"", charges, StringComparison.Ordinal);
        Assert.Contains("450", charges, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sharing_by_weight_needs_the_weights_and_a_receipt_that_is_posted()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Landed weight", Ct);
        var id = company.Id;
        var a = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "ROD", name = "Steel rod", uom = "ton", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var b = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "NAIL", name = "Nails", uom = "box", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var main = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "MAIN", name = "Main" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);
        var receipt = (await (await admin.PostAsJsonAsync(U("/api/stock/entries"), new
        {
            companyId = id, purpose = "Receipt", postingDate = "2026-11-10",
            lines = new[] { new { itemId = a!.Id, qty = 2m, rate = 50_000m, targetWarehouseId = main!.Id }, new { itemId = b!.Id, qty = 100m, rate = 200m, targetWarehouseId = main.Id } },
        }, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        var clearing = new object[] { new { description = "Clearing", amount = 1_000m, basis = "Weight" } };

        // A draft receipt is not goods received yet.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/stock/landed-costs"), new { receiptId = receipt.Id, postingDate = "2026-11-12", charges = clearing }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/stock/entries/{receipt.Id}/submit"), null, Ct)).StatusCode);

        // No weights: nothing to share by.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/stock/landed-costs"), new { receiptId = receipt.Id, postingDate = "2026-11-12", charges = clearing }, Ct)).StatusCode);

        // Rods weigh 2,000 kg and the nails 500 kg: 1,000 splits 800 and 200.
        var ok = await admin.PostAsJsonAsync(U("/api/stock/landed-costs"), new
        {
            receiptId = receipt.Id, postingDate = "2026-11-12", charges = clearing,
            lines = new[] { new { receiptLineIndex = 0, weight = 2_000m }, new { receiptLineIndex = 1, weight = 500m } },
        }, Ct);
        var cost = (await ok.Content.ReadFromJsonAsync<LandedCostDto>(Ct))!;
        Assert.Equal([800m, 200m], cost.Lines.Select(l => l.Allocated).ToArray());

        // Dated in the far future it is refused when submitted; dated before the receipt it is refused at once.
        var future = (await (await admin.PostAsJsonAsync(U("/api/stock/landed-costs"), new
        {
            receiptId = receipt.Id, postingDate = "2099-01-01", charges = new object[] { new { description = "Freight", amount = 10m, basis = "Quantity" } },
        }, Ct)).Content.ReadFromJsonAsync<LandedCostDto>(Ct))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/stock/landed-costs/{future.Id}/submit"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/stock/landed-costs"), new
        {
            receiptId = receipt.Id, postingDate = "2026-11-09", charges = new object[] { new { description = "Freight", amount = 10m, basis = "Quantity" } },
        }, Ct)).StatusCode);
    }
}
