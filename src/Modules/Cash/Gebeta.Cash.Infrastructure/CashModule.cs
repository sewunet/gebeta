using Gebeta.Application;
using Gebeta.Cash.Application;
using Gebeta.Cash.Contracts;
using Gebeta.Cash.Domain.Cashiers;
using Gebeta.Cash.Infrastructure.Persistence;
using Gebeta.Cash.Presentation;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Cash.Infrastructure;

/// <summary>
/// Cash Management (docs/modules/cash-management.md): cashiers with their own ledger and overdraw check,
/// receipts, payments, transfers, petty cash requests and automatic top-ups. Runs on Core alone; with
/// Accounting installed every movement becomes a general ledger journal.
/// </summary>
public sealed class CashModule : IErpModule
{
    public const string Id = "cash";

    public ModuleInfo Info { get; } = new(Id, "Cash Management", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["accounting", "hr", "payroll"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString("Main")
            ?? throw new ModuleLoadException("Connection string 'Main' is missing.");

        services.AddDbContext<CashDbContext>(options => CashDbContext.Configure(options, connectionString));
        services.AddScoped<IChangeHistory, Gebeta.Persistence.ModuleChangeHistory<CashDbContext>>(); // this module's records in /api/history
        services.AddScoped<ICashUnitOfWork>(sp => sp.GetRequiredService<CashDbContext>());
        services.AddScoped<ICashNumbers, CashNumbers>();
        services.AddScoped<ICashRepository, CashRepository>();
        services.AddScoped<IModuleMigrator, CashMigrator>();
        services.AddOutboxDispatcher<CashDbContext>(Id, CashDbContext.SchemaName);

        services.AddScoped<CashHandler>();
        services.AddScoped<ICommandHandler<UpdateCashSettingsCommand, CashSettingsDto>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<ICommandHandler<CreateCashierCommand, CashierDto>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<IQueryHandler<ListCashiersQuery, IReadOnlyList<CashierDto>>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<IQueryHandler<CashierLedgerQuery, IReadOnlyList<CashLedgerEntryDto>>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<ICommandHandler<CashTransactionCommand, CashMovementDto>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<ICommandHandler<CashTransferCommand, CashMovementDto>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<ICommandHandler<RequestPettyCashCommand, PettyCashRequestDto>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<ICommandHandler<ApprovePettyCashCommand, PettyCashRequestDto>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<ICommandHandler<RejectPettyCashCommand, PettyCashRequestDto>>(sp => sp.GetRequiredService<CashHandler>());
        services.AddScoped<IQueryHandler<ListPettyCashQuery, IReadOnlyList<PettyCashRequestDto>>>(sp => sp.GetRequiredService<CashHandler>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CashEndpoints.Map(endpoints);
}
