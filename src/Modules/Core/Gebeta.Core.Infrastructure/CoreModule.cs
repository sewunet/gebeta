using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Infrastructure.Persistence;
using Gebeta.Core.Presentation;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Core.Infrastructure;

/// <summary>
/// Core (Platform): the only required module. This class is the module's composition root:
/// it wires Domain, Application, Infrastructure and Presentation together.
/// </summary>
public sealed class CoreModule : IErpModule
{
    public const string Id = ModuleLoader.CoreModuleId;

    public ModuleInfo Info { get; } = new(Id, "Core", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [];
    public IReadOnlyList<string> Enhances { get; } = [];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;

        services.AddSingleton(TimeProvider.System);

        // Temporary in-memory store; replaced by EF Core + PostgreSQL in the next step.
        services.AddSingleton<InMemoryCoreStore>();
        services.AddScoped<ICompanyRepository, InMemoryCompanyRepository>();
        services.AddScoped<InMemoryCoreUnitOfWork>();
        services.AddScoped<ICoreUnitOfWork>(sp => sp.GetRequiredService<InMemoryCoreUnitOfWork>());

        services.AddScoped<ICommandHandler<CreateCompanyCommand, CompanyDto>, CreateCompanyHandler>();
        services.AddScoped<IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>>, ListCompaniesHandler>();
        services.AddScoped<IQueryHandler<GetCompanyQuery, CompanyDto>, GetCompanyHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => CoreEndpoints.Map(endpoints);
}
