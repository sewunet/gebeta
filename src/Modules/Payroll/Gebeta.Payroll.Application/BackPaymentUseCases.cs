using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Payroll.Application;

/// <summary>Payroll settings (configuration section <c>Payroll</c>).</summary>
public sealed class PayrollOptions
{
    public const string Section = "Payroll";

    public NegativeArrearsPolicy NegativeArrearsPolicy { get; set; } = NegativeArrearsPolicy.RecoverInPayroll;

    /// <summary>Components reported as basic salary, income tax and pension (defaults: the standard Ethiopian structure).</summary>
    public string BasicComponent { get; set; } = "BS";

    public IList<string> IncomeTaxComponents { get; } = ["IT"];
    public IList<string> EmployeePensionComponents { get; } = ["PE"];
    public IList<string> EmployerPensionComponents { get; } = ["PER"];
}

/// <param name="AssignmentId">The revised salary structure assignment (the new salary).</param>
/// <param name="PayrollDate">The arrears are paid with the slip of the month containing this date.</param>
public sealed record CreateBackPaymentCommand(
    Guid EmployeeId,
    Guid AssignmentId,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    DateOnly PayrollDate,
    string Reason,
    string? Reference) : ICommand<SalaryBackPaymentDto>;

public sealed record SubmitBackPaymentCommand(Guid BackPaymentId) : ICommand<SalaryBackPaymentDto>;

public sealed record CancelBackPaymentCommand(Guid BackPaymentId) : ICommand<SalaryBackPaymentDto>;

public sealed record GetBackPaymentQuery(Guid BackPaymentId) : IQuery<SalaryBackPaymentDto>;

public sealed record ListBackPaymentsQuery(Guid CompanyId) : IQuery<IReadOnlyList<SalaryBackPaymentDto>>;

public sealed class BackPaymentHandler(
    IBackPaymentRepository backPayments,
    ISalarySlipRepository slips,
    ISalaryStructureAssignmentRepository assignments,
    ISalaryStructureRepository structures,
    IEmployeeDirectory employees,
    ICompanyAccess access,
    IPayrollNumbers numbers,
    IPayrollUnitOfWork unitOfWork,
    PayrollOptions options,
    TimeProvider clock)
    : ICommandHandler<CreateBackPaymentCommand, SalaryBackPaymentDto>,
      ICommandHandler<SubmitBackPaymentCommand, SalaryBackPaymentDto>,
      ICommandHandler<CancelBackPaymentCommand, SalaryBackPaymentDto>,
      IQueryHandler<GetBackPaymentQuery, SalaryBackPaymentDto>,
      IQueryHandler<ListBackPaymentsQuery, IReadOnlyList<SalaryBackPaymentDto>>
{
    public const string DefaultPattern = "SBP-.FY.-.#####";

    public async Task<Result<SalaryBackPaymentDto>> Handle(CreateBackPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        var assignment = (await assignments.ListForEmployeeAsync(employee.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(a => a.Id == command.AssignmentId);
        if (assignment is null)
        {
            return Error.NotFound("Salary structure assignment", command.AssignmentId);
        }

        if (assignment.FromDate > command.EffectiveFrom)
        {
            return Error.Validation($"The revised salary applies from {assignment.FromDate:yyyy-MM-dd}; the back payment cannot start earlier.");
        }

        var structureDefinition = await structures.GetAsync(assignment.StructureId, cancellationToken).ConfigureAwait(false);
        if (structureDefinition is null)
        {
            return Error.NotFound("Salary structure", assignment.StructureId);
        }

        var structure = structureDefinition.Compile();
        var paidSlips = await slips.SubmittedForEmployeeAsync(employee.Id, command.EffectiveFrom, command.EffectiveTo, cancellationToken).ConfigureAwait(false);
        var earlier = await backPayments.SubmittedOverlappingAsync(employee.Id, command.EffectiveFrom, command.EffectiveTo, cancellationToken).ConfigureAwait(false);

        // What was really paid for each month: the latest earlier back payment, otherwise the slip.
        var baselines = paidSlips.Select(slip =>
        {
            var latest = earlier
                .SelectMany(bp => bp.Months.Where(m => m.PeriodStart == slip.PeriodStart).Select(m => (bp, m)))
                .OrderByDescending(x => x.bp.CreatedAt)
                .FirstOrDefault();
            return latest.m is not null
                ? BackPaymentCalculator.BaselineFromEarlier(latest.m, latest.bp.Name ?? string.Empty, slip.PaymentDays, slip.TotalWorkingDays)
                : BackPaymentCalculator.BaselineFromSlip(slip, structure);
        }).ToList();

        SalaryBackPayment backPayment;
        try
        {
            var months = BackPaymentCalculator.Calculate(structure, assignment.Base, baselines, EthiopianIncomeTax.ValidOn);
            backPayment = SalaryBackPayment.Create(employee.CompanyId, employee.Id, employee.Number, employee.FullName, assignment.Id, assignment.Base,
                command.EffectiveFrom, command.EffectiveTo, command.PayrollDate, command.Reason, command.Reference, months);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        backPayment.AssignName(await numbers.NextAsync(employee.CompanyId, SalaryBackPayment.Source, DefaultPattern, command.PayrollDate, cancellationToken).ConfigureAwait(false));
        backPayment.CreatedAt = clock.GetUtcNow();
        backPayments.Add(backPayment);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return BackPaymentMapping.ToDto(backPayment);
    }

    public async Task<Result<SalaryBackPaymentDto>> Handle(SubmitBackPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var backPayment = await AccessibleAsync(command.BackPaymentId, cancellationToken).ConfigureAwait(false);
        if (backPayment is null)
        {
            return Error.NotFound("Salary back payment", command.BackPaymentId);
        }

        try
        {
            backPayment.SubmitWithEntries(options.NegativeArrearsPolicy);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        backPayment.ModifiedAt = clock.GetUtcNow();
        unitOfWork.Publish(new SalaryBackPaymentSubmitted(backPayment.Id, backPayment.Name!, backPayment.CompanyId, backPayment.EmployeeId,
            backPayment.PayrollDate, backPayment.TotalGrossArrears, backPayment.TotalAdditionalTax, backPayment.NetArrears, backPayment.ModifiedAt.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return BackPaymentMapping.ToDto(backPayment);
    }

    public async Task<Result<SalaryBackPaymentDto>> Handle(CancelBackPaymentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var backPayment = await AccessibleAsync(command.BackPaymentId, cancellationToken).ConfigureAwait(false);
        if (backPayment is null)
        {
            return Error.NotFound("Salary back payment", command.BackPaymentId);
        }

        try
        {
            backPayment.Cancel();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        backPayment.ModifiedAt = clock.GetUtcNow();
        unitOfWork.Publish(new SalaryBackPaymentCancelled(backPayment.Id, backPayment.Name!, backPayment.CompanyId, backPayment.ModifiedAt.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return BackPaymentMapping.ToDto(backPayment);
    }

    public async Task<Result<SalaryBackPaymentDto>> Handle(GetBackPaymentQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var backPayment = await AccessibleAsync(query.BackPaymentId, cancellationToken).ConfigureAwait(false);
        return backPayment is null ? Error.NotFound("Salary back payment", query.BackPaymentId) : BackPaymentMapping.ToDto(backPayment);
    }

    public async Task<Result<IReadOnlyList<SalaryBackPaymentDto>>> Handle(ListBackPaymentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<SalaryBackPaymentDto>>(
            (await backPayments.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(BackPaymentMapping.ToDto).ToList());
    }

    private async Task<SalaryBackPayment?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var backPayment = await backPayments.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return backPayment is not null && await access.CanAccessAsync(backPayment.CompanyId, cancellationToken).ConfigureAwait(false) ? backPayment : null;
    }
}

internal static class BackPaymentMapping
{
    public static SalaryBackPaymentDto ToDto(SalaryBackPayment b) => new(
        b.Id, b.Name, b.CompanyId, b.EmployeeId, b.EmployeeNumber, b.EmployeeName, b.NewBase, b.EffectiveFrom, b.EffectiveTo, b.PayrollDate,
        b.Reason, b.Reference, b.DocStatus.ToString(),
        b.Months.Select(m =>
        {
            var start = EthiopianDate.FromGregorian(m.PeriodStart);
            return new BackPaymentMonthDto(m.PeriodStart, m.PeriodEnd, $"{EthiopianMonthNames.English(start.MonthName)} {start.Year}", m.Source, m.TaxTable,
                m.OldGross, m.NewGross, m.OldTax, m.NewTax, m.GrossArrears, m.AdditionalTax, m.OtherDeductionsArrears, m.EmployerContributionsArrears, m.NetArrears);
        }).ToList(),
        b.TotalGrossArrears, b.TotalAdditionalTax, b.TotalOtherDeductions, b.TotalEmployerContributions, b.NetArrears,
        b.Entries.Select(e => new AdditionalPayDto(e.Name, e.Abbreviation, e.Type.ToString(), e.Amount, e.SalarySlipId)).ToList());
}
