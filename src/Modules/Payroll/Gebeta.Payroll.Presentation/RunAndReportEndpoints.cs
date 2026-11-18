using Gebeta.Application;
using Gebeta.Modularity;
using Gebeta.Payroll.Application;
using Gebeta.Payroll.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Payroll.Presentation;

public static class RunAndReportEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/api/payroll/employees/{employeeId:guid}/profile", async (Guid employeeId, IQueryHandler<GetPayrollProfileQuery, PayrollProfileDto> handler,
                CancellationToken ct) => (await handler.Handle(new GetPayrollProfileQuery(employeeId), ct).ConfigureAwait(false)).ToHttp())
            .WithTags("Payroll")
            .WithName("GetPayrollProfile")
            .RequirePermission(PayrollPermissions.AssignmentsRead);

        endpoints.MapPut("/api/payroll/employees/{employeeId:guid}/profile", async (Guid employeeId, ProfileBody body,
                ICommandHandler<UpdatePayrollProfileCommand, PayrollProfileDto> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdatePayrollProfileCommand(employeeId, body.PaymentMethod, body.BankName, body.BankAccountNumber, body.PensionNumber, body.CostCenterId), ct)
                    .ConfigureAwait(false)).ToHttp())
            .WithTags("Payroll")
            .WithName("UpdatePayrollProfile")
            .RequirePermission(PayrollPermissions.ProfilesManage);

        var runs = endpoints.MapGroup("/api/payroll/runs").WithTags("Payroll: Runs");

        runs.MapPost("/", async (CreatePayrollRunCommand command, ICommandHandler<CreatePayrollRunCommand, PayrollRunDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/payroll/runs/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("CreatePayrollRun")
            .RequirePermission(PayrollPermissions.RunsManage);

        runs.MapGet("/{id:guid}/comparison", async (Guid id, string? format, IQueryHandler<PayrollComparisonQuery, PayrollComparisonDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(new PayrollComparisonQuery(id), ct).ConfigureAwait(false);
                return result.IsSuccess && CsvFile.Requested(format)
                    ? CsvFile.Create($"payroll-comparison-{result.Value.PeriodStart:yyyy-MM}.csv",
                        ["Employee No", "Employee Name", "Status", "Flagged", "Last Gross", "Last Tax", "Last Pension", "Last Net", "Gross", "Tax", "Pension", "Net",
                         "Net Change", "Change %", "Reasons"],
                        result.Value.Rows.Select(r => new object?[] { r.EmployeeNumber, r.EmployeeName, r.Status, r.Flagged ? "yes" : "", r.Previous?.Gross,
                            r.Previous?.IncomeTax, r.Previous?.EmployeePension, r.Previous?.Net, r.Current?.Gross, r.Current?.IncomeTax, r.Current?.EmployeePension,
                            r.Current?.Net, r.NetChange, r.ChangePercent, string.Join("; ", r.Reasons) }))
                    : result.ToHttp();
            })
            .WithName("PayrollComparison")
            .RequirePermission(PayrollPermissions.RunsRead);

        runs.MapGet("/{id:guid}/checks", async (Guid id, string? format, IQueryHandler<PayrollChecksQuery, PayrollChecksDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(new PayrollChecksQuery(id), ct).ConfigureAwait(false);
                return result.IsSuccess && CsvFile.Requested(format)
                    ? CsvFile.Create($"payroll-checks-{result.Value.PeriodStart:yyyy-MM}.csv", ["Employee No", "Employee Name", "Slip", "Severity", "Check", "Details"],
                        result.Value.Rows.Select(r => new object?[] { r.EmployeeNumber, r.EmployeeName, r.SlipNumber, r.Severity, r.Check, r.Message }))
                    : result.ToHttp();
            })
            .WithName("PayrollChecks")
            .RequirePermission(PayrollPermissions.RunsRead);

        runs.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetPayrollRunQuery, PayrollRunDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetPayrollRunQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetPayrollRun")
            .RequirePermission(PayrollPermissions.RunsRead);

        runs.MapPost("/{id:guid}/generate", async (Guid id, ICommandHandler<GeneratePayrollRunCommand, PayrollRunDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GeneratePayrollRunCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GeneratePayrollRun")
            .RequirePermission(PayrollPermissions.RunsManage);

        runs.MapPost("/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitPayrollRunCommand, PayrollRunDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SubmitPayrollRunCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitPayrollRun")
            .RequirePermission(PayrollPermissions.RunsManage);

        var reports = endpoints.MapGroup("/api/payroll/reports").WithTags("Payroll: Reports");

        reports.MapGet("/income-tax", async (Guid companyId, DateOnly periodStart, DateOnly periodEnd, string? format,
                IQueryHandler<IncomeTaxDeclarationQuery, IncomeTaxDeclarationDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(new IncomeTaxDeclarationQuery(companyId, periodStart, periodEnd), ct).ConfigureAwait(false);
                return result.IsSuccess && CsvFile.Requested(format)
                    ? CsvFile.Create($"income-tax-{periodStart:yyyy-MM}.csv",
                        ["Employee No", "Employee Name", "TIN", "Date of Joining", "Basic Salary", "Taxable Earnings", "Non-taxable Earnings", "Tax Withheld", "Employee Pension", "Net Pay"],
                        result.Value.Rows.Select(r => new object?[] { r.EmployeeNumber, r.EmployeeName, r.Tin, r.DateOfJoining, r.BasicSalary, r.TaxableEarnings,
                            r.NonTaxableEarnings, r.TaxWithheld, r.EmployeePension, r.NetPay }))
                    : result.ToHttp();
            })
            .WithName("IncomeTaxDeclaration")
            .RequirePermission(PayrollPermissions.ReportsRead);

        reports.MapGet("/pension", async (Guid companyId, DateOnly periodStart, DateOnly periodEnd, string? format,
                IQueryHandler<PensionScheduleQuery, PensionScheduleDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(new PensionScheduleQuery(companyId, periodStart, periodEnd), ct).ConfigureAwait(false);
                return result.IsSuccess && CsvFile.Requested(format)
                    ? CsvFile.Create($"pension-{periodStart:yyyy-MM}.csv",
                        ["Employee No", "Employee Name", "Pension No", "TIN", "Basic Salary", "Employee 7%", "Employer 11%", "Total"],
                        result.Value.Rows.Select(r => new object?[] { r.EmployeeNumber, r.EmployeeName, r.PensionNumber, r.Tin, r.BasicSalary,
                            r.EmployeeContribution, r.EmployerContribution, r.Total }))
                    : result.ToHttp();
            })
            .WithName("PensionSchedule")
            .RequirePermission(PayrollPermissions.ReportsRead);

        reports.MapGet("/bank-transfer", async (Guid companyId, DateOnly periodStart, DateOnly periodEnd, string? format,
                IQueryHandler<BankTransferQuery, BankTransferDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(new BankTransferQuery(companyId, periodStart, periodEnd), ct).ConfigureAwait(false);
                return result.IsSuccess && CsvFile.Requested(format)
                    ? CsvFile.Create($"bank-transfer-{periodStart:yyyy-MM}.csv",
                        ["Employee No", "Employee Name", "Bank", "Account Number", "Net Pay"],
                        result.Value.BankTransfers.Select(r => new object?[] { r.EmployeeNumber, r.EmployeeName, r.BankName, r.AccountNumber, r.NetPay }))
                    : result.ToHttp();
            })
            .WithName("BankTransfer")
            .RequirePermission(PayrollPermissions.ReportsRead);
    }
}

public sealed record ProfileBody(Gebeta.Payroll.Domain.Salary.PaymentMethod PaymentMethod, string? BankName, string? BankAccountNumber, string? PensionNumber,
    Guid? CostCenterId = null);
