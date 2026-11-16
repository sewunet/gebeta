using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;
using Gebeta.Selling.Contracts;
using Gebeta.Stock.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>A sales order followed through deliveries from Stock and the sales invoice in Accounting.</summary>
public class SalesOrderFlowTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task An_order_shows_what_is_delivered_and_invoiced_until_it_is_complete()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Seller PLC", abbreviation = "SEL", legalForm = "PrivateLimitedCompany", tin = "0800000141" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        await admin.PostAsync(U($"/api/accounting/companies/{id}/chart"), null, Ct);
        var customer = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Bole Builders", type = "Customer" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Only Supplier", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var cement = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "CEM", name = "Cement", uom = "bag", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var store = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "STORE", name = "Store" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        async Task<HttpResponseMessage> Stock(object body)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/stock/entries"), body, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct);
            return await admin.PostAsync(U($"/api/stock/entries/{draft!.Id}/submit"), null, Ct);
        }

        await Stock(new { companyId = id, purpose = "Receipt", postingDate = "2026-11-09", lines = new[] { new { itemId = cement!.Id, qty = 100m, rate = 1_000m, targetWarehouseId = store!.Id } } });

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/selling/sales-orders"), new
        {
            companyId = id, customerId = supplier!.Id, orderDate = "2026-11-10", lines = new[] { new { itemId = cement.Id, qty = 1m, rate = 1m } },
        }, Ct)).StatusCode);

        var order = await (await admin.PostAsJsonAsync(U("/api/selling/sales-orders"), new
        {
            companyId = id, customerId = customer!.Id, orderDate = "2026-11-10", deliveryDate = "2026-11-20",
            lines = new[] { new { itemId = cement.Id, qty = 40m, rate = 1_400m, warehouseId = store.Id } },
        }, Ct)).Content.ReadFromJsonAsync<SalesOrderDto>(Ct);
        Assert.Equal(("SO-2019-00001", 56_000m), (order!.Number, order.NetTotal));
        order = await (await admin.PostAsync(U($"/api/selling/sales-orders/{order.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<SalesOrderDto>(Ct);
        Assert.Equal("ToDeliverAndBill", order!.State);

        async Task<SalesOrderDto> Reload() => (await admin.GetFromJsonAsync<SalesOrderDto>(U($"/api/selling/sales-orders/{order!.Id}"), Ct))!;

        Assert.Equal(HttpStatusCode.OK, (await Stock(new
        {
            companyId = id, purpose = "Issue", postingDate = "2026-11-12", partyId = customer.Id, reference = order.Number,
            lines = new[] { new { itemId = cement.Id, qty = 25m, sourceWarehouseId = store.Id } },
        })).StatusCode);
        order = await Reload();
        Assert.Equal(("ToDeliverAndBill", 62.5m), (order.State, order.PercentDelivered));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/selling/sales-orders/{order.Id}/cancel"), null, Ct)).StatusCode);

        await Stock(new
        {
            companyId = id, purpose = "Issue", postingDate = "2026-11-13", partyId = customer.Id, reference = order.Number,
            lines = new[] { new { itemId = cement.Id, qty = 15m, sourceWarehouseId = store.Id } },
        });
        Assert.Equal("ToBill", (await Reload()).State);

        var invoice = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = id, partyId = customer.Id, postingDate = "2026-11-13", orderReference = order.Number,
            lines = new object[] { new { itemId = cement.Id, description = "Cement", quantity = 40, rate = 1_400, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        await admin.PostAsync(U($"/api/accounting/invoices/{invoice!.Id}/submit"), null, Ct);
        order = await Reload();
        Assert.Equal(("Completed", 100m, 100m), (order.State, order.PercentDelivered, order.PercentBilled));

        // A credit note for 10 bags puts the order back to "to bill" for them.
        var note = await (await admin.PostAsJsonAsync(U($"/api/accounting/invoices/{invoice.Id}/return"), new
        {
            postingDate = "2026-11-14", lines = new object[] { new { itemId = cement.Id, description = "Cement", quantity = 10, rate = 1_400, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        await admin.PostAsync(U($"/api/accounting/invoices/{note!.Id}/submit"), null, Ct);
        order = await Reload();
        Assert.Equal(("ToBill", 30m), (order.State, order.Lines.Single().BilledQty));
    }
}
