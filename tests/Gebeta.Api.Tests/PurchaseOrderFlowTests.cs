using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Buying.Contracts;
using Gebeta.Core.Contracts;
using Gebeta.Stock.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>A purchase order followed through goods received in Stock and the supplier's invoice in Accounting.</summary>
public class PurchaseOrderFlowTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task An_order_shows_what_is_received_and_billed_until_it_is_complete()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Buyer PLC", abbreviation = "BUY", legalForm = "PrivateLimitedCompany", tin = "0800000131" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        await admin.PostAsync(U($"/api/accounting/companies/{id}/chart"), null, Ct);
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Addis Cement Factory", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var customer = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "A Customer", type = "Customer" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var cement = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "CEM", name = "Cement", uom = "bag", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var sand = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "SAND", name = "Sand", uom = "m3", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var store = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "STORE", name = "Store" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = id, supplierId = customer!.Id, orderDate = "2026-11-10", lines = new[] { new { itemId = cement!.Id, qty = 1m, rate = 1m } },
        }, Ct)).StatusCode);

        var order = await (await admin.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = id, supplierId = supplier!.Id, orderDate = "2026-11-10", requiredBy = "2026-11-17",
            lines = new object[] { new { itemId = cement.Id, qty = 100m, rate = 500m, warehouseId = store!.Id }, new { itemId = sand!.Id, qty = 20m, rate = 300m } },
        }, Ct)).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct);
        Assert.Equal(("PO-2019-00001", "Draft", 56_000m), (order!.Number, order.State, order.NetTotal));
        order = await (await admin.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct);
        Assert.Equal("ToReceiveAndBill", order!.State);

        async Task Receive(params object[] lines)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/stock/entries"), new
            {
                companyId = id, purpose = "Receipt", postingDate = "2026-11-12", partyId = supplier.Id, reference = order!.Number, lines,
            }, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct);
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync(U($"/api/stock/entries/{draft!.Id}/submit"), null, Ct)).StatusCode);
        }

        async Task<PurchaseOrderDto> Reload() => (await admin.GetFromJsonAsync<PurchaseOrderDto>(U($"/api/buying/purchase-orders/{order!.Id}"), Ct))!;

        await Receive(new { itemId = cement.Id, qty = 60m, rate = 500m, targetWarehouseId = store.Id });
        order = await Reload();
        Assert.Equal(("ToReceiveAndBill", 50m, 0m), (order.State, order.PercentReceived, order.PercentBilled));
        Assert.Equal(60m, order.Lines.Single(l => l.ItemCode == "CEM").ReceivedQty);

        // A received order cannot be cancelled, only closed.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/cancel"), null, Ct)).StatusCode);

        await Receive(new { itemId = cement.Id, qty = 40m, rate = 500m, targetWarehouseId = store.Id }, new { itemId = sand.Id, qty = 20m, rate = 300m, targetWarehouseId = store.Id });
        Assert.Equal("ToBill", (await Reload()).State);

        // The supplier's invoice names the order. A draft does not count; the submitted invoice completes the order.
        var bill = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = id, partyId = supplier.Id, postingDate = "2026-11-14", supplierInvoiceNumber = "ACF-200", orderReference = order.Number,
            lines = new object[]
            {
                new { itemId = cement.Id, description = "Cement", quantity = 100, rate = 500, accountCode = "2150", isService = false },
                new { itemId = sand.Id, description = "Sand", quantity = 20, rate = 300, accountCode = "2150", isService = false },
            },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal(order.Number, bill!.OrderReference);
        Assert.Equal("ToBill", (await Reload()).State);
        await admin.PostAsync(U($"/api/accounting/invoices/{bill.Id}/submit"), null, Ct);
        order = await Reload();
        Assert.Equal(("Completed", 100m, 100m), (order.State, order.PercentReceived, order.PercentBilled));

        var list = await admin.GetFromJsonAsync<List<PurchaseOrderDto>>(U($"/api/buying/purchase-orders?companyId={id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal("Completed", Assert.Single(list!).State);
    }
}
