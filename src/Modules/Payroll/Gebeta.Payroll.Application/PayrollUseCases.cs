using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Payroll.Application;

public interface IPayrollUnitOfWork : IUnitOfWork;

/// <summary>Payroll's gap-free document numbers (salary slips).</summary>
public interface IPayrollNumbers : IDocumentNumbers;

/// <summary>Payment days from the installed provider (HR) or Payroll's own fallback.</summary>
public interface IPayrollPaymentDays
{
    Task<decimal?> GetAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, decimal totalWorkingDays, CancellationToken cancellationToken);
}

// ---------------------------------------------------------------- salary structures

/// <param name="Variables">Pay values the formulas use besides the base (e.g. ctc, fuel_payment); every assignment then gives them.</param>
public sealed record CreateSalaryStructureCommand(Guid CompanyId, string Name, IReadOnlyList<SalaryComponentDto>? Components, IReadOnlyList<string>? Variables = null)
    : ICommand<SalaryStructureDto>;

public sealed record ListSalaryStructuresQuery(Guid CompanyId) : IQuery<IReadOnlyList<SalaryStructureDto>>;

public sealed class SalaryStructureHandler(ISalaryStructureRepository structures, ICompanyDirectory companies, ICompanyAccess access,
    IPayrollUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateSalaryStructureCommand, SalaryStructureDto>, IQueryHandler<ListSalaryStructuresQuery, IReadOnlyList<SalaryStructureDto>>
{
    public async Task<Result<SalaryStructureDto>> Handle(CreateSalaryStructureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)
            || await companies.GetAsync(command.CompanyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var lines = command.Components is null || command.Components.Count == 0
            ? EthiopianSalaryStructures.Standard.ToList()
            : new List<SalaryComponentLine>();
        foreach (var c in command.Components ?? [])
        {
            if (!Enum.TryParse<ComponentType>(c.Type, ignoreCase: true, out var type))
            {
                return Error.Validation($"{c.Name}: type must be Earning, Deduction or EmployerContribution.");
            }

            lines.Add(new SalaryComponentLine(c.Name, c.Abbreviation, type, c.Formula, c.Amount, c.Condition, c.IsTaxable, c.DependsOnPaymentDays));
        }

        var created = SalaryStructureDefinition.Create(command.CompanyId, command.Name, lines, command.Variables);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        if (await structures.NameExistsAsync(command.CompanyId, created.Value.Name, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"The company already has a salary structure named '{created.Value.Name}'.");
        }

        created.Value.CreatedAt = clock.GetUtcNow();
        structures.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PayrollMapping.ToDto(created.Value);
    }

    public async Task<Result<IReadOnlyList<SalaryStructureDto>>> Handle(ListSalaryStructuresQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<SalaryStructureDto>>(
            (await structures.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(PayrollMapping.ToDto).ToList());
    }
}

// ---------------------------------------------------------------- assignments

/// <param name="Values">The structure's pay values for this employee (all of them, nothing else).</param>
public sealed record AssignSalaryStructureCommand(Guid EmployeeId, Guid StructureId, decimal Base, DateOnly FromDate, IReadOnlyDictionary<string, decimal>? Values = null)
    : ICommand<SalaryStructureAssignmentDto>;

public sealed record ListAssignmentsQuery(Guid EmployeeId) : IQuery<IReadOnlyList<SalaryStructureAssignmentDto>>;

public sealed class AssignmentHandler(ISalaryStructureAssignmentRepository assignments, ISalaryStructureRepository structures,
    IEmployeeDirectory employees, ICompanyAccess access, IPayrollUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<AssignSalaryStructureCommand, SalaryStructureAssignmentDto>, IQueryHandler<ListAssignmentsQuery, IReadOnlyList<SalaryStructureAssignmentDto>>
{
    public async Task<Result<SalaryStructureAssignmentDto>> Handle(AssignSalaryStructureCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        var structure = await structures.GetAsync(command.StructureId, cancellationToken).ConfigureAwait(false);
        if (structure is null || structure.CompanyId != employee.CompanyId)
        {
            return Error.NotFound("Salary structure", command.StructureId);
        }

        var existing = await assignments.ListForEmployeeAsync(employee.Id, cancellationToken).ConfigureAwait(false);
        if (existing.Any(a => a.FromDate == command.FromDate))
        {
            return Error.Conflict($"{employee.FullName} already has an assignment from {command.FromDate:yyyy-MM-dd}.");
        }

        var values = structure.CheckValues(command.Values);
        if (values.IsFailure)
        {
            return values.Error!;
        }

        var created = SalaryStructureAssignment.Create(employee.CompanyId, employee.Id, structure.Id, command.Base, command.FromDate, command.Values);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        created.Value.CreatedAt = clock.GetUtcNow();
        assignments.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PayrollMapping.ToDto(created.Value);
    }

    public async Task<Result<IReadOnlyList<SalaryStructureAssignmentDto>>> Handle(ListAssignmentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var employee = await employees.GetAsync(query.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", query.EmployeeId);
        }

        return Result.Success<IReadOnlyList<SalaryStructureAssignmentDto>>(
            (await assignments.ListForEmployeeAsync(employee.Id, cancellationToken).ConfigureAwait(false)).Select(PayrollMapping.ToDto).ToList());
    }
}

// ---------------------------------------------------------------- salary slips

/// <param name="PaymentDays">Optional; otherwise from HR attendance, or from joining/leaving dates.</param>
/// <param name="TotalWorkingDays">Days in the month for payroll; Ethiopian practice is 30.</param>
public sealed record CreateSalarySlipCommand(
    Guid EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? PaymentDays = null,
    decimal TotalWorkingDays = 30,
    IReadOnlyDictionary<string, decimal>? Additional = null) : ICommand<SalarySlipDto>;

public sealed record SubmitSalarySlipCommand(Guid SlipId) : ICommand<SalarySlipDto>;

public sealed record CancelSalarySlipCommand(Guid SlipId) : ICommand<SalarySlipDto>;

public sealed record GetSalarySlipQuery(Guid SlipId) : IQuery<SalarySlipDto>;

public sealed record ListSalarySlipsQuery(Guid CompanyId, DateOnly? From, DateOnly? To) : IQuery<IReadOnlyList<SalarySlipDto>>;

public sealed class SalarySlipHandler(
    ISalarySlipRepository slips,
    ISalaryStructureAssignmentRepository assignments,
    ISalaryStructureRepository structures,
    IEmployeeDirectory employees,
    ICompanyAccess access,
    IPayrollPaymentDays paymentDays,
    IAdditionalPayRepository additionalPay,
    IPayrollNumbers numbers,
    IPayrollUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreateSalarySlipCommand, SalarySlipDto>,
      ICommandHandler<SubmitSalarySlipCommand, SalarySlipDto>,
      ICommandHandler<CancelSalarySlipCommand, SalarySlipDto>,
      IQueryHandler<GetSalarySlipQuery, SalarySlipDto>,
      IQueryHandler<ListSalarySlipsQuery, IReadOnlyList<SalarySlipDto>>
{
    public const string DocumentType = "Salary Slip";
    public const string DefaultPattern = "SAL-.FY.-.#####";

    public async Task<Result<SalarySlipDto>> Handle(CreateSalarySlipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        if (command.PeriodEnd < command.PeriodStart || command.PeriodEnd.DayNumber - command.PeriodStart.DayNumber > 40)
        {
            return Error.Validation("A salary slip covers one month at most.");
        }

        if (employee.DateOfJoining > command.PeriodEnd || employee.DateOfLeaving < command.PeriodStart)
        {
            return Error.Validation($"{employee.FullName} was not employed between {command.PeriodStart:yyyy-MM-dd} and {command.PeriodEnd:yyyy-MM-dd}.");
        }

        if (await slips.HasActiveSlipAsync(employee.Id, command.PeriodStart, command.PeriodEnd, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"{employee.FullName} already has a salary slip for this period. Cancel it first to make a new one.");
        }

        var assignment = await assignments.InForceOnAsync(employee.Id, command.PeriodEnd, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Error.Validation($"{employee.FullName} has no salary structure assignment for this period.");
        }

        var structure = await structures.GetAsync(assignment.StructureId, cancellationToken).ConfigureAwait(false);
        if (structure is null)
        {
            return Error.NotFound("Salary structure", assignment.StructureId);
        }

        var days = command.PaymentDays
            ?? await paymentDays.GetAsync(employee.Id, command.PeriodStart, command.PeriodEnd, command.TotalWorkingDays, cancellationToken).ConfigureAwait(false)
            ?? EmployedDays(employee, command.PeriodStart, command.PeriodEnd, command.TotalWorkingDays);

        // Back payment arrears (and other additional pay) due in this period.
        var pending = await additionalPay.PendingAsync(employee.Id, command.PeriodStart, command.PeriodEnd, cancellationToken).ConfigureAwait(false);

        SalarySlip slip;
        try
        {
            slip = SalarySlip.Calculate(employee.CompanyId, employee.Id, employee.Number, employee.FullName, command.PeriodStart, command.PeriodEnd,
                structure, assignment, days, command.TotalWorkingDays, command.Additional, EthiopianIncomeTax.ValidOn(command.PeriodEnd),
                pending.Select(e => e.ToSlipLine()).ToList());
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        slip.AssignName(await numbers.NextAsync(employee.CompanyId, DocumentType, DefaultPattern, command.PeriodEnd, cancellationToken).ConfigureAwait(false));
        slip.CreatedAt = clock.GetUtcNow();
        slips.Add(slip);
        foreach (var entry in pending)
        {
            entry.IncludeIn(slip.Id);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return PayrollMapping.ToDto(slip);
    }

    public async Task<Result<SalarySlipDto>> Handle(SubmitSalarySlipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var slip = await AccessibleSlipAsync(command.SlipId, cancellationToken).ConfigureAwait(false);
        if (slip is null)
        {
            return Error.NotFound("Salary slip", command.SlipId);
        }

        try
        {
            slip.Submit();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        slip.ModifiedAt = clock.GetUtcNow();
        unitOfWork.Publish(new SalarySlipSubmitted(slip.Id, slip.Name!, slip.CompanyId, slip.EmployeeId, slip.PeriodStart, slip.PeriodEnd,
            slip.GrossPay, slip.TotalDeductions, slip.NetPay, slip.EmployerContributions,
            slip.Lines.Select(l => new SalarySlipLineData(l.Abbreviation, l.Component, l.Type.ToString(), l.Amount)).ToList(),
            slip.ModifiedAt.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PayrollMapping.ToDto(slip);
    }

    public async Task<Result<SalarySlipDto>> Handle(CancelSalarySlipCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var slip = await AccessibleSlipAsync(command.SlipId, cancellationToken).ConfigureAwait(false);
        if (slip is null)
        {
            return Error.NotFound("Salary slip", command.SlipId);
        }

        try
        {
            slip.Cancel();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        // Arrears in a cancelled slip wait for the corrected slip.
        foreach (var entry in await additionalPay.ForSlipAsync(slip.Id, cancellationToken).ConfigureAwait(false))
        {
            entry.Release();
        }

        slip.ModifiedAt = clock.GetUtcNow();
        unitOfWork.Publish(new SalarySlipCancelled(slip.Id, slip.Name!, slip.CompanyId, slip.ModifiedAt.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PayrollMapping.ToDto(slip);
    }

    public async Task<Result<SalarySlipDto>> Handle(GetSalarySlipQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var slip = await AccessibleSlipAsync(query.SlipId, cancellationToken).ConfigureAwait(false);
        return slip is null ? Error.NotFound("Salary slip", query.SlipId) : PayrollMapping.ToDto(slip);
    }

    public async Task<Result<IReadOnlyList<SalarySlipDto>>> Handle(ListSalarySlipsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<SalarySlipDto>>(
            (await slips.ListAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false)).Select(PayrollMapping.ToDto).ToList());
    }

    private async Task<SalarySlip?> AccessibleSlipAsync(Guid id, CancellationToken cancellationToken)
    {
        var slip = await slips.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return slip is not null && await access.CanAccessAsync(slip.CompanyId, cancellationToken).ConfigureAwait(false) ? slip : null;
    }

    /// <summary>Days employed in the period, counted on the 30-day month: a full period is paid in full.</summary>
    internal static decimal EmployedDays(EmployeeInfo employee, DateOnly periodStart, DateOnly periodEnd, decimal totalWorkingDays)
    {
        var from = employee.DateOfJoining > periodStart ? employee.DateOfJoining : periodStart;
        var to = employee.DateOfLeaving is { } left && left < periodEnd ? left : periodEnd;
        if (from == periodStart && to == periodEnd)
        {
            return totalWorkingDays;
        }

        return Math.Min(totalWorkingDays, Math.Max(0, to.DayNumber - from.DayNumber + 1));
    }
}

internal static class PayrollMapping
{
    public static SalaryStructureDto ToDto(SalaryStructureDefinition s) => new(s.Id, s.CompanyId, s.Name, s.IsActive,
        s.Components.Select(c => new SalaryComponentDto(c.Name, c.Abbreviation, c.Type.ToString(), c.Formula, c.Amount, c.Condition, c.IsTaxable, c.DependsOnPaymentDays)).ToList(),
        s.Variables);

    public static SalaryStructureAssignmentDto ToDto(SalaryStructureAssignment a) => new(a.Id, a.EmployeeId, a.StructureId, a.Base, a.FromDate, a.Values);

    public static SalarySlipDto ToDto(SalarySlip s)
    {
        var start = EthiopianDate.FromGregorian(s.PeriodStart);
        return new SalarySlipDto(s.Id, s.Name, s.CompanyId, s.EmployeeId, s.EmployeeNumber, s.EmployeeName, s.PeriodStart, s.PeriodEnd,
            $"{EthiopianMonthNames.English(start.MonthName)} {start.Year}", s.DocStatus.ToString(), s.StructureName, s.Base, s.PaymentDays, s.TotalWorkingDays,
            s.Lines.Select(l => new SalaryLineDto(l.Component, l.Abbreviation, l.Type.ToString(), l.Amount, l.Formula)).ToList(),
            s.GrossPay, s.TaxableEarnings, s.TotalDeductions, s.NetPay, s.EmployerContributions, s.TaxTable, s.IsHistory);
    }
}
