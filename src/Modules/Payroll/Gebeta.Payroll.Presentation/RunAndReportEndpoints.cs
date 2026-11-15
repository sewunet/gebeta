using System.Globalization;
using System.Text;
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

        endpoints.MapPut("/api/payroll/employees/{employeeId:guid}/profile", async (Guid employeeId, ProfileBody body,
                ICommandHandler<UpdatePayrollProfileCommand, PayrollProfileDto> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdatePayrollProfileCommand(employeeId, body.PaymentMethod, body.BankName, body.BankAccountNumber, body.PensionNumber), ct)
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
                return result.IsSuccess && IsCsv(format)
                    ? Csv($"income-tax-{periodStart:yyyy-MM}.csv",
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
                return result.IsSuccess && IsCsv(format)
                    ? Csv($"pension-{periodStart:yyyy-MM}.csv",
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
                return result.IsSuccess && IsCsv(format)
                    ? Csv($"bank-transfer-{periodStart:yyyy-MM}.csv",
                        ["Employee No", "Employee Name", "Bank", "Account Number", "Net Pay"],
                        result.Value.BankTransfers.Select(r => new object?[] { r.EmployeeNumber, r.EmployeeName, r.BankName, r.AccountNumber, r.NetPay }))
                    : result.ToHttp();
            })
            .WithName("BankTransfer")
            .RequirePermission(PayrollPermissions.ReportsRead);
    }

    private static bool IsCsv(string? format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>UTF-8 CSV with a byte-order mark, so Excel shows Amharic names correctly.</summary>
    private static IResult Csv(string fileName, string[] header, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Quote)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', row.Select(v => Quote(v switch
            {
                null => string.Empty,
                decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty,
            }))));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}

public sealed record ProfileBody(Gebeta.Payroll.Domain.Salary.PaymentMethod PaymentMethod, string? BankName, string? BankAccountNumber, string? PensionNumber);
