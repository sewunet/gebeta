using Gebeta.Application;
using Gebeta.Modularity;
using Gebeta.Stock.Application;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Domain.Ledger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Stock.Presentation;

public static class StockEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var st = endpoints.MapGroup("/api/stock").WithTags("Stock");

        st.MapGet("/warehouses", async (Guid companyId, IQueryHandler<ListWarehousesQuery, IReadOnlyList<WarehouseDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListWarehousesQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListWarehouses").RequirePermission(StockPermissions.WarehousesRead);
        st.MapPost("/warehouses", async (CreateWarehouseCommand command, ICommandHandler<CreateWarehouseCommand, WarehouseDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateWarehouse").RequirePermission(StockPermissions.WarehousesManage);

        st.MapGet("/items/{itemId:guid}/settings", async (Guid itemId, IQueryHandler<GetItemStockSettingQuery, ItemStockSettingDto> h, CancellationToken ct) =>
                (await h.Handle(new GetItemStockSettingQuery(itemId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetItemStockSetting").RequirePermission(StockPermissions.WarehousesRead);
        st.MapPut("/items/{itemId:guid}/settings", async (Guid itemId, ValuationBody body, ICommandHandler<SetItemValuationCommand, ItemStockSettingDto> h, CancellationToken ct) =>
                (await h.Handle(new SetItemValuationCommand(itemId, body.Method), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SetItemValuation").RequirePermission(StockPermissions.WarehousesManage);

        st.MapGet("/entries", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ListStockEntriesQuery, IReadOnlyList<StockEntryDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListStockEntriesQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListStockEntries").RequirePermission(StockPermissions.EntriesRead);
        st.MapGet("/entries/{id:guid}", async (Guid id, IQueryHandler<GetStockEntryQuery, StockEntryDto> h, CancellationToken ct) =>
                (await h.Handle(new GetStockEntryQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetStockEntry").RequirePermission(StockPermissions.EntriesRead);
        st.MapPost("/entries", async (CreateStockEntryCommand command, ICommandHandler<CreateStockEntryCommand, StockEntryDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateStockEntry").RequirePermission(StockPermissions.EntriesCreate);
        st.MapPost("/entries/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitStockEntryCommand, StockEntryDto> h, CancellationToken ct) =>
                (await h.Handle(new SubmitStockEntryCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitStockEntry").RequirePermission(StockPermissions.EntriesSubmit);
        st.MapPost("/entries/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelStockEntryCommand, StockEntryDto> h, CancellationToken ct) =>
                (await h.Handle(new CancelStockEntryCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelStockEntry").RequirePermission(StockPermissions.EntriesSubmit);

        st.MapGet("/landed-costs", async (Guid receiptId, IQueryHandler<ListLandedCostsQuery, IReadOnlyList<LandedCostDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListLandedCostsQuery(receiptId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListLandedCosts").RequirePermission(StockPermissions.LandedCostsRead);
        st.MapGet("/landed-costs/{id:guid}", async (Guid id, IQueryHandler<GetLandedCostQuery, LandedCostDto> h, CancellationToken ct) =>
                (await h.Handle(new GetLandedCostQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetLandedCost").RequirePermission(StockPermissions.LandedCostsRead);
        st.MapPost("/landed-costs", async (CreateLandedCostCommand command, ICommandHandler<CreateLandedCostCommand, LandedCostDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateLandedCost").RequirePermission(StockPermissions.LandedCostsCreate);
        st.MapPost("/landed-costs/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitLandedCostCommand, LandedCostDto> h, CancellationToken ct) =>
                (await h.Handle(new SubmitLandedCostCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitLandedCost").RequirePermission(StockPermissions.LandedCostsSubmit);
        st.MapPost("/landed-costs/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelLandedCostCommand, LandedCostDto> h, CancellationToken ct) =>
                (await h.Handle(new CancelLandedCostCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelLandedCost").RequirePermission(StockPermissions.LandedCostsSubmit);

        st.MapGet("/reports/balance", async (Guid companyId, DateOnly asOf, IQueryHandler<StockBalanceQuery, StockBalanceDto> h, CancellationToken ct) =>
                (await h.Handle(new StockBalanceQuery(companyId, asOf), ct).ConfigureAwait(false)).ToHttp())
            .WithName("StockBalance").RequirePermission(StockPermissions.ReportsRead);
        st.MapGet("/reports/ledger", async (Guid companyId, Guid itemId, Guid? warehouseId, DateOnly from, DateOnly to, IQueryHandler<StockLedgerQuery, StockLedgerDto> h,
                CancellationToken ct) =>
                (await h.Handle(new StockLedgerQuery(companyId, itemId, warehouseId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("StockLedger").RequirePermission(StockPermissions.ReportsRead);
    }
}

public sealed record ValuationBody(ValuationMethod Method);
