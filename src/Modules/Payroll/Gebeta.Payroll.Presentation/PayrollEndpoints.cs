using Gebeta.Application;
using Gebeta.Modularity;
using Gebeta.Payroll.Application;
using Gebeta.Payroll.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Payroll.Presentation;

public static class PayrollEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var payroll = endpoints.MapGroup("/api/payroll").WithTags("Payroll");

        payroll.MapPost("/calculator", async (CalculateSalaryRequest request, IQueryHandler<CalculateSalaryQuery, SalaryCalculationDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CalculateSalaryQuery(request), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CalculateSalary")
            .RequirePermission(PayrollPermissions.CalculatorUse);

        payroll.MapGet("/tax-tables", async (IQueryHandler<ListTaxTablesQuery, IReadOnlyList<TaxTableDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListTaxTablesQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListTaxTables")
            .RequirePermission(PayrollPermissions.TaxTablesRead);
    }
}
