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

        var additional = endpoints.MapGroup("/api/payroll/additional-salaries").WithTags("Payroll: Additional salary");
        additional.MapGet("/", async (Guid companyId, DateOnly from, DateOnly to, IQueryHandler<ListAdditionalSalariesQuery, IReadOnlyList<AdditionalSalaryDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListAdditionalSalariesQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListAdditionalSalaries").RequirePermission(PayrollPermissions.AdditionalRead);
        additional.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetAdditionalSalaryQuery, AdditionalSalaryDto> h, CancellationToken ct) =>
                (await h.Handle(new GetAdditionalSalaryQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetAdditionalSalary").RequirePermission(PayrollPermissions.AdditionalRead);
        additional.MapPost("/", async (CreateAdditionalSalaryCommand command, ICommandHandler<CreateAdditionalSalaryCommand, AdditionalSalaryDto> h, CancellationToken ct) =>
                Created(await h.Handle(command, ct).ConfigureAwait(false), r => $"/api/payroll/additional-salaries/{r.Id}"))
            .WithName("CreateAdditionalSalary").RequirePermission(PayrollPermissions.AdditionalManage);
        additional.MapPost("/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitAdditionalSalaryCommand, AdditionalSalaryDto> h, CancellationToken ct) =>
                (await h.Handle(new SubmitAdditionalSalaryCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitAdditionalSalary").RequirePermission(PayrollPermissions.AdditionalManage);
        additional.MapPost("/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelAdditionalSalaryCommand, AdditionalSalaryDto> h, CancellationToken ct) =>
                (await h.Handle(new CancelAdditionalSalaryCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelAdditionalSalary").RequirePermission(PayrollPermissions.AdditionalManage);

        var loans = endpoints.MapGroup("/api/payroll/loans").WithTags("Payroll: Loans and standing deductions");
        loans.MapGet("/", async (Guid companyId, Guid? employeeId, IQueryHandler<ListEmployeeLoansQuery, IReadOnlyList<EmployeeLoanDto>> h, CancellationToken ct) =>
                (await h.Handle(new ListEmployeeLoansQuery(companyId, employeeId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListEmployeeLoans").RequirePermission(PayrollPermissions.LoansRead);
        loans.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetEmployeeLoanQuery, EmployeeLoanDto> h, CancellationToken ct) =>
                (await h.Handle(new GetEmployeeLoanQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetEmployeeLoan").RequirePermission(PayrollPermissions.LoansRead);
        loans.MapPost("/", async (CreateEmployeeLoanCommand command, ICommandHandler<CreateEmployeeLoanCommand, EmployeeLoanDto> h, CancellationToken ct) =>
                Created(await h.Handle(command, ct).ConfigureAwait(false), r => $"/api/payroll/loans/{r.Id}"))
            .WithName("CreateEmployeeLoan").RequirePermission(PayrollPermissions.LoansManage);
        loans.MapPost("/{id:guid}/{action:regex(^(submit|pause|resume|cancel)$)}", async (Guid id, string action, ICommandHandler<EmployeeLoanActionCommand, EmployeeLoanDto> h,
                CancellationToken ct) =>
                (await h.Handle(new EmployeeLoanActionCommand(id, Enum.Parse<LoanAction>(action, ignoreCase: true)), ct).ConfigureAwait(false)).ToHttp())
            .WithName("EmployeeLoanAction").RequirePermission(PayrollPermissions.LoansManage);
        loans.MapPost("/{id:guid}/settle", async (Guid id, SettleLoanBody body, ICommandHandler<SettleEmployeeLoanCommand, EmployeeLoanDto> h, CancellationToken ct) =>
                (await h.Handle(new SettleEmployeeLoanCommand(id, body.Date, body.Reference), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SettleEmployeeLoan").RequirePermission(PayrollPermissions.LoansManage);

        // Self-service: always the signed-in employee's own payslips.
        payroll.MapGet("/my/salary-slips", async (IQueryHandler<MySalarySlipsQuery, IReadOnlyList<SalarySlipDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new MySalarySlipsQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("MySalarySlips").RequirePermission(PayrollPermissions.SelfRead);
        payroll.MapGet("/my/salary-slips/{id:guid}", async (Guid id, IQueryHandler<MySalarySlipQuery, SalarySlipDto> handler, CancellationToken ct) =>
                (await handler.Handle(new MySalarySlipQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("MySalarySlip").RequirePermission(PayrollPermissions.SelfRead);

        payroll.MapPost("/calculator", async (CalculateSalaryRequest request, IQueryHandler<CalculateSalaryQuery, SalaryCalculationDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CalculateSalaryQuery(request), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CalculateSalary")
            .RequirePermission(PayrollPermissions.CalculatorUse);

        payroll.MapGet("/tax-tables", async (IQueryHandler<ListTaxTablesQuery, IReadOnlyList<TaxTableDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListTaxTablesQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListTaxTables")
            .RequirePermission(PayrollPermissions.TaxTablesRead);

        // Salary structures
        payroll.MapGet("/structures", async (Guid companyId, IQueryHandler<ListSalaryStructuresQuery, IReadOnlyList<SalaryStructureDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListSalaryStructuresQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListSalaryStructures")
            .RequirePermission(PayrollPermissions.StructuresRead);

        payroll.MapPost("/structures", async (CreateSalaryStructureCommand command, ICommandHandler<CreateSalaryStructureCommand, SalaryStructureDto> handler, CancellationToken ct) =>
                Created(await handler.Handle(command, ct).ConfigureAwait(false), r => $"/api/payroll/structures/{r.Id}"))
            .WithName("CreateSalaryStructure")
            .RequirePermission(PayrollPermissions.StructuresManage);

        // Assignments
        payroll.MapGet("/employees/{employeeId:guid}/assignments", async (Guid employeeId, IQueryHandler<ListAssignmentsQuery, IReadOnlyList<SalaryStructureAssignmentDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListAssignmentsQuery(employeeId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListAssignments")
            .RequirePermission(PayrollPermissions.AssignmentsRead);

        payroll.MapPost("/assignments", async (AssignSalaryStructureCommand command, ICommandHandler<AssignSalaryStructureCommand, SalaryStructureAssignmentDto> handler, CancellationToken ct) =>
                Created(await handler.Handle(command, ct).ConfigureAwait(false), r => $"/api/payroll/employees/{r.EmployeeId}/assignments"))
            .WithName("AssignSalaryStructure")
            .RequirePermission(PayrollPermissions.AssignmentsManage);

        // Salary slips
        payroll.MapGet("/salary-slips", async (Guid companyId, DateOnly? from, DateOnly? to, IQueryHandler<ListSalarySlipsQuery, IReadOnlyList<SalarySlipDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListSalarySlipsQuery(companyId, from, to), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListSalarySlips")
            .RequirePermission(PayrollPermissions.SlipsRead);

        payroll.MapGet("/salary-slips/{id:guid}", async (Guid id, IQueryHandler<GetSalarySlipQuery, SalarySlipDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetSalarySlipQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetSalarySlip")
            .RequirePermission(PayrollPermissions.SlipsRead);

        payroll.MapPost("/salary-slips", async (CreateSalarySlipCommand command, ICommandHandler<CreateSalarySlipCommand, SalarySlipDto> handler, CancellationToken ct) =>
                Created(await handler.Handle(command, ct).ConfigureAwait(false), r => $"/api/payroll/salary-slips/{r.Id}"))
            .WithName("CreateSalarySlip")
            .RequirePermission(PayrollPermissions.SlipsCreate);

        payroll.MapPost("/salary-slips/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitSalarySlipCommand, SalarySlipDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SubmitSalarySlipCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitSalarySlip")
            .RequirePermission(PayrollPermissions.SlipsSubmit);

        payroll.MapPost("/salary-slips/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelSalarySlipCommand, SalarySlipDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CancelSalarySlipCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelSalarySlip")
            .RequirePermission(PayrollPermissions.SlipsCancel);

        MapBackPayments(endpoints);
        RunAndReportEndpoints.Map(endpoints);
    }

    public static void MapBackPayments(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var backPayments = endpoints.MapGroup("/api/payroll/back-payments").WithTags("Payroll: Back payments");

        backPayments.MapGet("/", async (Guid companyId, IQueryHandler<ListBackPaymentsQuery, IReadOnlyList<SalaryBackPaymentDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListBackPaymentsQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListBackPayments")
            .RequirePermission(PayrollPermissions.BackPaymentsRead);

        backPayments.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetBackPaymentQuery, SalaryBackPaymentDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetBackPaymentQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetBackPayment")
            .RequirePermission(PayrollPermissions.BackPaymentsRead);

        backPayments.MapPost("/", async (CreateBackPaymentCommand command, ICommandHandler<CreateBackPaymentCommand, SalaryBackPaymentDto> handler, CancellationToken ct) =>
                Created(await handler.Handle(command, ct).ConfigureAwait(false), r => $"/api/payroll/back-payments/{r.Id}"))
            .WithName("CreateBackPayment")
            .RequirePermission(PayrollPermissions.BackPaymentsCreate);

        backPayments.MapPost("/{id:guid}/submit", async (Guid id, ICommandHandler<SubmitBackPaymentCommand, SalaryBackPaymentDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SubmitBackPaymentCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SubmitBackPayment")
            .RequirePermission(PayrollPermissions.BackPaymentsSubmit);

        backPayments.MapPost("/{id:guid}/cancel", async (Guid id, ICommandHandler<CancelBackPaymentCommand, SalaryBackPaymentDto> handler, CancellationToken ct) =>
                (await handler.Handle(new CancelBackPaymentCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CancelBackPayment")
            .RequirePermission(PayrollPermissions.BackPaymentsCancel);
    }

    private static IResult Created<T>(Gebeta.SharedKernel.Result<T> result, Func<T, string> location) =>
        result.IsSuccess ? Results.Created(location(result.Value), result.Value) : result.ToHttp();
}

/// <summary>Body of a loan settlement: the date, and the receipt or order reference.</summary>
public sealed record SettleLoanBody(DateOnly Date, string? Reference);

