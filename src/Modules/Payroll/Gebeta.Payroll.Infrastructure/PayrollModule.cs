using Gebeta.Application;
using Gebeta.Modularity;
using Gebeta.Payroll.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Presentation;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Payroll.Infrastructure;

/// <summary>
/// Payroll (docs/modules/payroll.md). Runs on Core alone; HR adds payment days from attendance,
/// Accounting posts the payroll journal, Cash Management pays cash salaries.
/// </summary>
public sealed class PayrollModule : IErpModule
{
    public const string Id = "payroll";

    public ModuleInfo Info { get; } = new(Id, "Payroll", new Version(0, 1, 0));
    public IReadOnlyList<string> Requires { get; } = [ModuleLoader.CoreModuleId];
    public IReadOnlyList<string> Enhances { get; } = ["hr", "accounting", "cash"];

    public void Register(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var services = context.Services;
        services.AddScoped<IQueryHandler<CalculateSalaryQuery, SalaryCalculationDto>, CalculateSalaryHandler>();
        services.AddScoped<IQueryHandler<ListTaxTablesQuery, IReadOnlyList<TaxTableDto>>, ListTaxTablesHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => PayrollEndpoints.Map(endpoints);
}
