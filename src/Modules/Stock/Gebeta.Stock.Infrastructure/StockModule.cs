using Gebeta.Application;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Gebeta.Stock.Application;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Domain.Ledger;
using Gebeta.Stock.Infrastructure.Persistence;
using Gebeta.Stock.Presentation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Stock.Infrastructure;

/// <summary>
/// Stock (docs/modules/stock.md): warehouses, stock entries, the stock ledger with FIFO or moving-average valuation,
/// and stock reports. Runs on Core alone; with Accounting installed each entry's value is posted to the general ledger.
/// </summary>
public sealed class StockModule : IErpModule
{
    public const string Id = "stock";

    public ModuleInfo Info { get; } = new(Id, "Stock", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["accounting", "buying", "selling"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString("Main")
            ?? throw new ModuleLoadException("Connection string 'Main' is missing.");

        services.AddDbContext<StockDbContext>(options => StockDbContext.Configure(options, connectionString));
        services.AddScoped<IStockUnitOfWork>(sp => sp.GetRequiredService<StockDbContext>());
        services.AddScoped<IStockNumbers, StockNumbers>();
        services.AddScoped<IStockRepository, StockRepository>();
        services.AddScoped<IModuleMigrator, StockMigrator>();
        services.AddOutboxDispatcher<StockDbContext>(Id, StockDbContext.SchemaName);
        context.Capabilities.Provide<Gebeta.Buying.Contracts.IPurchaseReceiptLookup, StockPurchaseReceipts>()
            .Provide<Gebeta.Selling.Contracts.ISalesDeliveryLookup, StockSalesDeliveries>();

        services.AddScoped<StockHandler>();
        services.AddScoped<IQueryHandler<ListWarehousesQuery, IReadOnlyList<WarehouseDto>>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<ICommandHandler<CreateWarehouseCommand, WarehouseDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<IQueryHandler<GetItemStockSettingQuery, ItemStockSettingDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<ICommandHandler<SetItemValuationCommand, ItemStockSettingDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<ICommandHandler<CreateStockEntryCommand, StockEntryDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<ICommandHandler<SubmitStockEntryCommand, StockEntryDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<ICommandHandler<CancelStockEntryCommand, StockEntryDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<IQueryHandler<GetStockEntryQuery, StockEntryDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<IQueryHandler<ListStockEntriesQuery, IReadOnlyList<StockEntryDto>>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<IQueryHandler<StockBalanceQuery, StockBalanceDto>>(sp => sp.GetRequiredService<StockHandler>());
        services.AddScoped<IQueryHandler<StockLedgerQuery, StockLedgerDto>>(sp => sp.GetRequiredService<StockHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => StockEndpoints.Map(endpoints);
}
