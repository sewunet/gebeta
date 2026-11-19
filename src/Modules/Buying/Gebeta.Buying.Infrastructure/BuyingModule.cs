using Gebeta.Application;
using Gebeta.Buying.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Buying.Domain.Orders;
using Gebeta.Buying.Infrastructure.Persistence;
using Gebeta.Buying.Presentation;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Buying.Infrastructure;

/// <summary>
/// Buying: purchase orders to suppliers. Runs on Core alone; with Stock installed it sees the goods received against
/// each order, with Accounting the quantities billed (capabilities they provide, with empty fallbacks).
/// </summary>
public sealed class BuyingModule : IErpModule
{
    public const string Id = "buying";

    public ModuleInfo Info { get; } = new(Id, "Buying", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["stock", "accounting"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString("Main")
            ?? throw new ModuleLoadException("Connection string 'Main' is missing.");

        services.AddDbContext<BuyingDbContext>(options => BuyingDbContext.Configure(options, connectionString));
        services.AddScoped<IChangeHistory, Gebeta.Persistence.ModuleChangeHistory<BuyingDbContext>>(); // this module's records in /api/history
        services.AddScoped<IRecordPermissions, Persistence.BuyingRecordPermissions>(); // which permission guards a record, for its attachments
        services.AddScoped<IRelatedDocuments, Persistence.BuyingRelatedDocuments>(); // links in /api/documents/{id}/related
        services.AddScoped<IBuyingUnitOfWork>(sp => sp.GetRequiredService<BuyingDbContext>());
        services.AddScoped<IBuyingNumbers, BuyingNumbers>();
        services.AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>();
        services.AddScoped<IModuleMigrator, BuyingMigrator>();
        services.AddOutboxDispatcher<BuyingDbContext>(Id, BuyingDbContext.SchemaName);
        context.Capabilities
            .UseWithFallback<IPurchaseReceiptLookup, NoReceipts>()
            .UseWithFallback<IPurchaseBillingLookup, NoBilling>();
        services.AddScoped<IOrderFulfilment, OrderFulfilment>();

        services.AddScoped<PurchaseOrderHandler>();
        services.AddScoped<ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderDto>>(sp => sp.GetRequiredService<PurchaseOrderHandler>());
        services.AddScoped<ICommandHandler<PurchaseOrderActionCommand, PurchaseOrderDto>>(sp => sp.GetRequiredService<PurchaseOrderHandler>());
        services.AddScoped<IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto>>(sp => sp.GetRequiredService<PurchaseOrderHandler>());
        services.AddScoped<IQueryHandler<ListPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderDto>>>(sp => sp.GetRequiredService<PurchaseOrderHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => BuyingEndpoints.Map(endpoints);
}
