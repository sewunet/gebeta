using Gebeta.Application;
using Gebeta.Selling.Application;
using Gebeta.Selling.Contracts;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Selling.Presentation;

public static class SellingEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var po = endpoints.MapGroup("/api/selling/sales-orders").WithTags("Selling");

        po.MapGet("/", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ListSalesOrdersQuery, IReadOnlyList<SalesOrderDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListSalesOrdersQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListSalesOrders").RequirePermission(SellingPermissions.OrdersRead);
        po.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetSalesOrderQuery, SalesOrderDto> h, CancellationToken ct) =>
                (await h.Handle(new GetSalesOrderQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetSalesOrder").RequirePermission(SellingPermissions.OrdersRead);
        po.MapPost("/", async (CreateSalesOrderCommand command, ICommandHandler<CreateSalesOrderCommand, SalesOrderDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateSalesOrder").RequirePermission(SellingPermissions.OrdersCreate);
        foreach (var action in Enum.GetValues<SalesOrderAction>())
        {
            po.MapPost($"/{{id:guid}}/{action.ToString().ToLowerInvariant()}", async (Guid id, ICommandHandler<SalesOrderActionCommand, SalesOrderDto> h, CancellationToken ct) =>
                    (await h.Handle(new SalesOrderActionCommand(id, action), ct).ConfigureAwait(false)).ToHttp())
                .WithName($"{action}SalesOrder").RequirePermission(SellingPermissions.OrdersSubmit);
        }
    }
}
