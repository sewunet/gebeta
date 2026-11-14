using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Infrastructure.Persistence;
using Gebeta.Core.Presentation;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Core.Infrastructure;

/// <summary>
/// Core (Platform): the only required module. This class is the module's composition root:
/// it wires Domain, Application, Infrastructure and Presentation together.
/// </summary>
public sealed class CoreModule : IErpModule
{
    public const string Id = ModuleLoader.CoreModuleId;
    public const string ConnectionStringName = "Main";

    public ModuleInfo Info { get; } = new(Id, "Core", new Version(0, 2, 0));
    public IReadOnlyList<string> Requires { get; } = [];
    public IReadOnlyList<string> Enhances { get; } = [];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        var connectionString = context.Configuration.GetConnectionString(ConnectionStringName)
            ?? throw new ModuleLoadException(
                $"Connection string '{ConnectionStringName}' is missing (ConnectionStrings:{ConnectionStringName} or ConnectionStrings__{ConnectionStringName}).");

        services.AddSingleton(TimeProvider.System);

        services.AddDbContext<CoreDbContext>(options => ConfigureDbContext(options, connectionString));
        services.AddScoped<ICoreUnitOfWork>(sp => sp.GetRequiredService<CoreDbContext>());
        services.AddScoped<ICompanyRepository, CompanyRepository>();
        services.AddScoped<IModuleMigrator, CoreMigrator>();
        services.AddHealthChecks().AddCheck<CoreDatabaseHealthCheck>("core-database");

        services.AddScoped<ICommandHandler<CreateCompanyCommand, CompanyDto>, CreateCompanyHandler>();
        services.AddScoped<IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>>, ListCompaniesHandler>();
        services.AddScoped<IQueryHandler<GetCompanyQuery, CompanyDto>, GetCompanyHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CoreEndpoints.Map(endpoints);

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(CoreDbContext.MigrationsHistoryTable, CoreDbContext.Schema))
            .UseSnakeCaseNamingConvention();
}
