using Gebeta.Application;
using Gebeta.Buying.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Buying.Presentation;

public static class BuyingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var po = endpoints.MapGroup("/api/buying/purchase-orders").WithTags("Buying");

        po.MapGet("/", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ListPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListPurchaseOrdersQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListPurchaseOrders").RequirePermission(BuyingPermissions.OrdersRead);
        po.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto> h, CancellationToken ct) =>
                (await h.Handle(new GetPurchaseOrderQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetPurchaseOrder").RequirePermission(BuyingPermissions.OrdersRead);
        po.MapPost("/", async (CreatePurchaseOrderCommand command, ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreatePurchaseOrder").RequirePermission(BuyingPermissions.OrdersCreate);
        foreach (var action in Enum.GetValues<PurchaseOrderAction>())
        {
            po.MapPost($"/{{id:guid}}/{action.ToString().ToLowerInvariant()}", async (Guid id, ICommandHandler<PurchaseOrderActionCommand, PurchaseOrderDto> h, CancellationToken ct) =>
                    (await h.Handle(new PurchaseOrderActionCommand(id, action), ct).ConfigureAwait(false)).ToHttp())
                .WithName($"{action}PurchaseOrder").RequirePermission(BuyingPermissions.OrdersSubmit);
        }

        var ag = endpoints.MapGroup("/api/buying/agreements").WithTags("Buying");
        ag.MapGet("/", async (Guid companyId, Guid? supplierId, IQueryHandler<ListBlanketAgreementsQuery, IReadOnlyList<BlanketAgreementDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListBlanketAgreementsQuery(companyId, supplierId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListBlanketAgreements").RequirePermission(BuyingPermissions.AgreementsRead);
        ag.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetBlanketAgreementQuery, BlanketAgreementDto> h, CancellationToken ct) =>
                (await h.Handle(new GetBlanketAgreementQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetBlanketAgreement").RequirePermission(BuyingPermissions.AgreementsRead);
        ag.MapPost("/", async (CreateBlanketAgreementCommand command, ICommandHandler<CreateBlanketAgreementCommand, BlanketAgreementDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateBlanketAgreement").RequirePermission(BuyingPermissions.AgreementsCreate);
        foreach (var action in Enum.GetValues<BlanketAgreementAction>())
        {
            ag.MapPost($"/{{id:guid}}/{action.ToString().ToLowerInvariant()}", async (Guid id, ICommandHandler<BlanketAgreementActionCommand, BlanketAgreementDto> h, CancellationToken ct) =>
                    (await h.Handle(new BlanketAgreementActionCommand(id, action), ct).ConfigureAwait(false)).ToHttp())
                .WithName($"{action}BlanketAgreement").RequirePermission(BuyingPermissions.AgreementsSubmit);
        }
    }
}
