using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Stock.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Goods received against a supplier's invoice through 2150, and goods delivered to a customer at cost of sales.</summary>
public class GoodsFlowTests(GebetaApiFactory factory) : IClassFixture<GebetaApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private async Task Deliver<TConsumer, TEvent>(TEvent integrationEvent)
        where TConsumer : IIntegrationEventConsumer<TEvent>
        where TEvent : class
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IAccountingUnitOfWork>();
        await using var transaction = await unitOfWork.BeginTransactionAsync(Ct);
        await scope.ServiceProvider.GetRequiredService<TConsumer>().HandleAsync(integrationEvent, Ct);
        await transaction.CommitAsync(Ct);
    }

    [Fact]
    public async Task Received_goods_are_cleared_by_the_supplier_invoice_and_delivered_goods_cost_sales()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Goods PLC", abbreviation = "GDS", legalForm = "PrivateLimitedCompany", tin = "0800000121" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;
        await admin.PostAsync(U($"/api/accounting/companies/{id}/chart"), null, Ct);
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Addis Cement Factory", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var customer = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = id, name = "Bole Builders", type = "Customer" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct);
        var cement = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "CEM", name = "Cement", uom = "bag", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct);
        var store = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "STORE", name = "Store" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        async Task<StockEntryDto> Stock(object body)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/stock/entries"), body, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct);
            return (await (await admin.PostAsync(U($"/api/stock/entries/{draft!.Id}/submit"), null, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        }

        // Goods received from the supplier: Dr 1300 / Cr 2150.
        var received = await Stock(new
        {
            companyId = id, purpose = "Receipt", postingDate = "2026-11-10", partyId = supplier!.Id, reference = "DN-4471",
            lines = new[] { new { itemId = cement!.Id, qty = 10m, rate = 1_000m, targetWarehouseId = store!.Id } },
        });
        Assert.Equal(("2150", "Addis Cement Factory", "DN-4471"), (received.CounterAccountCode, received.PartyName, received.Reference));
        await Deliver<StockJournalRequestedConsumer, StockJournalRequested>(new StockJournalRequested(received.Id, received.Number!, id, received.PostingDate, "Goods received",
            [new StockJournalLine("1300", 10_000m, 0m), new StockJournalLine("2150", 0m, 10_000m)], DateTimeOffset.UtcNow));

        // The supplier's invoice for the same goods debits 2150 (not an expense).
        var bill = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Purchase", companyId = id, partyId = supplier.Id, postingDate = "2026-11-12", supplierInvoiceNumber = "ACF-118",
            lines = new object[] { new { itemId = cement.Id, description = "Cement", quantity = 10, rate = 1_000, accountCode = "2150", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        Assert.Equal("CEM", bill!.Lines.Single().ItemCode);
        await admin.PostAsync(U($"/api/accounting/invoices/{bill.Id}/submit"), null, Ct);

        // Goods delivered to the customer: Dr 5000 / Cr 1300, at the stock's value.
        var delivered = await Stock(new
        {
            companyId = id, purpose = "Issue", postingDate = "2026-11-14", partyId = customer!.Id, reference = "SINV-pending",
            lines = new[] { new { itemId = cement.Id, qty = 4m, sourceWarehouseId = store.Id } },
        });
        Assert.Equal(("5000", 4_000m), (delivered.CounterAccountCode, delivered.TotalValue));
        await Deliver<StockJournalRequestedConsumer, StockJournalRequested>(new StockJournalRequested(delivered.Id, delivered.Number!, id, delivered.PostingDate, "Goods delivered",
            [new StockJournalLine("5000", 4_000m, 0m), new StockJournalLine("1300", 0m, 4_000m)], DateTimeOffset.UtcNow));
        var sale = await (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = id, partyId = customer.Id, postingDate = "2026-11-14",
            lines = new object[] { new { itemId = cement.Id, description = "Cement", quantity = 4, rate = 1_400, accountCode = "4100", isService = false } },
        }, Ct)).Content.ReadFromJsonAsync<InvoiceDto>(Ct);
        await admin.PostAsync(U($"/api/accounting/invoices/{sale!.Id}/submit"), null, Ct);

        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={id}&asOf=2026-11-30"), Ct);
        decimal Balance(string code) => tb!.Rows.SingleOrDefault(r => r.Code == code)?.Balance ?? 0m;
        Assert.Equal(0m, Balance("2150"));        // received and billed: cleared
        Assert.Equal(6_000m, Balance("1300"));    // 6 bags left at 1,000
        Assert.Equal(10_000m, Balance("2100"));   // owed to the supplier
        Assert.Equal(4_000m, Balance("5000"));    // cost of the 4 bags sold
        Assert.Equal(5_600m, Balance("4100"));    // sales
        var pl = await admin.GetFromJsonAsync<ProfitAndLossDto>(U($"/api/accounting/reports/profit-and-loss?companyId={id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal(1_600m, pl!.NetProfit);      // gross margin on the sale

        // Refused: a party on a transfer; another company's item on an invoice.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/stock/entries"), new
        {
            companyId = id, purpose = "Transfer", postingDate = "2026-11-14", partyId = customer.Id,
            lines = new[] { new { itemId = cement.Id, qty = 1m, sourceWarehouseId = store.Id, targetWarehouseId = Guid.CreateVersion7() } },
        }, Ct)).StatusCode);
        var other = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Other Goods PLC", abbreviation = "OGP", legalForm = "PrivateLimitedCompany", tin = "0800000122" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var foreignItem = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = other!.Id, code = "X", name = "X", uom = "pcs", isStockItem = true }, Ct))
            .Content.ReadFromJsonAsync<ItemDto>(Ct);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/accounting/invoices"), new
        {
            kind = "Sales", companyId = id, partyId = customer.Id, postingDate = "2026-11-14",
            lines = new object[] { new { itemId = foreignItem!.Id, description = "X", quantity = 1, rate = 1, accountCode = "4100", isService = false } },
        }, Ct)).StatusCode);
    }
}
