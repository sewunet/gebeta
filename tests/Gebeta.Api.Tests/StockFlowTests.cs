using System.Net;
using System.Net.Http.Json;
using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Stock.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>Items, warehouses, FIFO stock entries, the stock reports and the value journals in Accounting.</summary>
public class StockFlowTests(GebetaApiFactory factory)
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
    public async Task Fifo_stock_moves_through_warehouses_and_its_value_reaches_the_ledger()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Stock PLC", abbreviation = "STK", legalForm = "PrivateLimitedCompany", tin = "0800000101" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var id = company!.Id;

        var cement = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "cement-50", name = "Cement 50 kg", uom = "bag", group = "Building", isStockItem = true }, Ct))
            .Content.ReadFromJsonAsync<ItemDto>(Ct);
        Assert.Equal("CEMENT-50", cement!.Code);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "CEMENT-50", name = "Again", uom = "bag", isStockItem = true }, Ct)).StatusCode);
        var service = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = id, code = "DELIVERY", name = "Delivery", uom = "trip", isStockItem = false }, Ct))
            .Content.ReadFromJsonAsync<ItemDto>(Ct);

        var main = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "main", name = "Main store" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);
        var shop = await (await admin.PostAsJsonAsync(U("/api/stock/warehouses"), new { companyId = id, code = "SHOP", name = "Shop" }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);
        Assert.Equal("1300", main!.StockAccountCode);
        Assert.Equal("Fifo", (await (await admin.PutAsJsonAsync(U($"/api/stock/items/{cement.Id}/settings"), new { method = "Fifo" }, Ct)).Content.ReadFromJsonAsync<ItemStockSettingDto>(Ct))!.Method);

        async Task<StockEntryDto> Entry(object body, bool submit = true)
        {
            var draft = await (await admin.PostAsJsonAsync(U("/api/stock/entries"), body, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct);
            if (!submit)
            {
                return draft!;
            }

            var response = await admin.PostAsync(U($"/api/stock/entries/{draft!.Id}/submit"), null, Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<StockEntryDto>(Ct))!;
        }

        object Line(decimal qty, decimal? rate = null, Guid? from = null, Guid? to = null) =>
            new { itemId = cement.Id, qty, rate, sourceWarehouseId = from, targetWarehouseId = to };

        var r1 = await Entry(new { companyId = id, purpose = "Receipt", postingDate = "2026-11-10", lines = new[] { Line(100m, 500m, to: main.Id) } });
        Assert.Equal("STE-2019-00001", r1.Number);
        Assert.Equal(50_000m, r1.TotalValue);
        await Entry(new { companyId = id, purpose = "Receipt", postingDate = "2026-11-11", lines = new[] { Line(50m, 560m, to: main.Id) } });
        var transfer = await Entry(new { companyId = id, purpose = "Transfer", postingDate = "2026-11-12", lines = new[] { Line(120m, from: main.Id, to: shop!.Id) } });
        Assert.Equal(61_200m, transfer.TotalValue); // FIFO: 100 × 500 + 20 × 560
        var issue = await Entry(new { companyId = id, purpose = "Issue", postingDate = "2026-11-13", remark = "Site A", lines = new[] { Line(30m, from: shop.Id) } });
        Assert.Equal(15_300m, issue.TotalValue); // the shop's layer came in at 510

        // Refused: more than in stock, a service item, a method change.
        var tooMuch = await Entry(new { companyId = id, purpose = "Issue", postingDate = "2026-11-13", lines = new[] { Line(500m, from: shop.Id) } }, submit: false);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/stock/entries/{tooMuch.Id}/submit"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(U("/api/stock/entries"),
            new { companyId = id, purpose = "Receipt", postingDate = "2026-11-13", lines = new[] { new { itemId = service!.Id, qty = 1m, rate = 1m, targetWarehouseId = main.Id } } }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(U($"/api/stock/items/{cement.Id}/settings"), new { method = "MovingAverage" }, Ct)).StatusCode);

        var balance = await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={id}&asOf=2026-11-30"), Ct);
        Assert.Equal((30m, 16_800m), balance!.Rows.Where(r => r.Warehouse == "MAIN").Select(r => (r.Qty, r.Value)).Single());
        Assert.Equal((90m, 45_900m), balance.Rows.Where(r => r.Warehouse == "SHOP").Select(r => (r.Qty, r.Value)).Single());
        Assert.Equal(62_700m, balance.TotalValue);
        var ledger = await admin.GetFromJsonAsync<StockLedgerDto>(U($"/api/stock/reports/ledger?companyId={id}&itemId={cement.Id}&from=2026-11-01&to=2026-11-30"), Ct);
        Assert.Equal(5, ledger!.Rows.Count); // two receipts, transfer out and in, issue

        // Cancelling: without the first receipt the transfer would take MAIN below zero; the issue can go.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/stock/entries/{r1.Id}/cancel"), null, Ct)).StatusCode);
        Assert.Equal("Cancelled", (await (await admin.PostAsync(U($"/api/stock/entries/{issue.Id}/cancel"), null, Ct)).Content.ReadFromJsonAsync<StockEntryDto>(Ct))!.Status);
        balance = await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={id}&asOf=2026-11-30"), Ct);
        Assert.Equal(120m, balance!.Rows.Single(r => r.Warehouse == "SHOP").Qty);

        // The value journals, as Accounting receives them over RabbitMQ.
        await admin.PostAsync(U($"/api/accounting/companies/{id}/chart"), null, Ct);
        await Deliver<StockJournalRequestedConsumer, StockJournalRequested>(new StockJournalRequested(r1.Id, r1.Number!, id, r1.PostingDate, "Stock receipt",
            [new StockJournalLine("1300", 50_000m, 0m), new StockJournalLine("5100", 0m, 50_000m)], DateTimeOffset.UtcNow));
        await Deliver<StockJournalRequestedConsumer, StockJournalRequested>(new StockJournalRequested(issue.Id, issue.Number!, id, issue.PostingDate, "Stock issue",
            [new StockJournalLine("5100", 15_300m, 0m), new StockJournalLine("1300", 0m, 15_300m)], DateTimeOffset.UtcNow));
        var tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={id}&asOf=2026-11-30"), Ct);
        Assert.Equal(34_700m, tb!.Rows.Single(r => r.Code == "1300").Balance);
        await Deliver<StockJournalAdjustedConsumer, StockJournalAdjusted>(new StockJournalAdjusted(Guid.CreateVersion7(), issue.Id, issue.Number!, id, issue.PostingDate,
            $"Cancelled {issue.Number}", [new StockJournalLine("1300", 15_300m, 0m), new StockJournalLine("5100", 0m, 15_300m)], DateTimeOffset.UtcNow));
        tb = await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={id}&asOf=2026-11-30"), Ct);
        Assert.Equal(50_000m, tb!.Rows.Single(r => r.Code == "1300").Balance);
    }
}
