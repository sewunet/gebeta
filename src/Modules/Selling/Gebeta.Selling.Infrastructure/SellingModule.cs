using Gebeta.Application;
using Gebeta.Selling.Application;
using Gebeta.Selling.Contracts;
using Gebeta.Selling.Domain.Orders;
using Gebeta.Selling.Infrastructure.Persistence;
using Gebeta.Selling.Presentation;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Selling.Infrastructure;

/// <summary>
/// Selling: sales orders from customers. Runs on Core alone; with Stock installed it sees the goods delivered against
/// each order, with Accounting the quantities invoiced (capabilities they provide, with empty fallbacks).
/// </summary>
public sealed class SellingModule : IErpModule
{
    public const string Id = "selling";

    public ModuleInfo Info { get; } = new(Id, "Selling", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["stock", "accounting"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString("Main")
            ?? throw new ModuleLoadException("Connection string 'Main' is missing.");

        services.AddDbContext<SellingDbContext>(options => SellingDbContext.Configure(options, connectionString));
        services.AddScoped<IChangeHistory, Gebeta.Persistence.ModuleChangeHistory<SellingDbContext>>(); // this module's records in /api/history
        services.AddScoped<IRecordPermissions, Persistence.SellingRecordPermissions>(); // which permission guards a record, for its attachments
        services.AddScoped<IRelatedDocuments, Persistence.SellingRelatedDocuments>(); // links in /api/documents/{id}/related
        services.AddScoped<ISellingUnitOfWork>(sp => sp.GetRequiredService<SellingDbContext>());
        services.AddScoped<ISellingNumbers, SellingNumbers>();
        services.AddScoped<ISalesOrderRepository, SalesOrderRepository>();
        services.AddScoped<IModuleMigrator, SellingMigrator>();
        services.AddOutboxDispatcher<SellingDbContext>(Id, SellingDbContext.SchemaName);
        context.Capabilities
            .UseWithFallback<ISalesDeliveryLookup, NoDeliveries>()
            .UseWithFallback<ISalesBillingLookup, NoBilling>();
        services.AddScoped<IOrderFulfilment, OrderFulfilment>();

        services.AddScoped<SalesOrderHandler>();
        services.AddScoped<ICommandHandler<CreateSalesOrderCommand, SalesOrderDto>>(sp => sp.GetRequiredService<SalesOrderHandler>());
        services.AddScoped<ICommandHandler<SalesOrderActionCommand, SalesOrderDto>>(sp => sp.GetRequiredService<SalesOrderHandler>());
        services.AddScoped<IQueryHandler<GetSalesOrderQuery, SalesOrderDto>>(sp => sp.GetRequiredService<SalesOrderHandler>());
        services.AddScoped<IQueryHandler<ListSalesOrdersQuery, IReadOnlyList<SalesOrderDto>>>(sp => sp.GetRequiredService<SalesOrderHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => SellingEndpoints.Map(endpoints);
}
