using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Application;

/// <param name="Lines">The slip's lines as paid; types are Earning, Deduction or EmployerContribution.</param>
/// <param name="TaxableEarnings">The earnings income tax was worked out on (the lines do not say which were taxed).</param>
/// <param name="Number">The slip's number in the previous system, kept so it can be found again.</param>
/// <param name="StructureId">The migrated structure it was paid with, when there is one.</param>
/// <param name="Additional">What was paid on top of the formulas that month (already inside the lines); back payments pay it again.</param>
public sealed record RecordSalarySlipHistoryCommand(
    Guid EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    Guid? StructureId,
    string StructureName,
    decimal Base,
    decimal PaymentDays,
    decimal TotalWorkingDays,
    IReadOnlyList<SalaryLineDto> Lines,
    decimal TaxableEarnings,
    string Number,
    IReadOnlyList<SlipAdditionalDto>? Additional = null) : ICommand<SalarySlipDto>;

/// <summary>
/// Salary slips paid before the cut-over, kept as history (docs/data-migration.md step 5). Nothing is published: the payroll
/// journal of those months is already in the opening balances.
/// </summary>
public sealed class SlipHistoryHandler(ISalarySlipRepository slips, ISalaryStructureRepository structures, IEmployeeDirectory employees, ICompanyAccess access,
    IPayrollUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<RecordSalarySlipHistoryCommand, SalarySlipDto>
{
    public async Task<Result<SalarySlipDto>> Handle(RecordSalarySlipHistoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        if (command.StructureId is { } structureId
            && (await structures.GetAsync(structureId, cancellationToken).ConfigureAwait(false))?.CompanyId != employee.CompanyId)
        {
            return Error.NotFound("Salary structure", structureId);
        }

        if (await slips.HasActiveSlipAsync(employee.Id, command.PeriodStart, command.PeriodEnd, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"{employee.FullName} already has a salary slip for {command.PeriodStart:yyyy-MM-dd} – {command.PeriodEnd:yyyy-MM-dd}.");
        }

        if (await slips.NumberExistsAsync(employee.CompanyId, command.Number?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"Salary slip {command.Number} is already recorded.");
        }

        var lines = new List<SalarySlipLine>();
        foreach (var l in command.Lines ?? [])
        {
            if (!Enum.TryParse<ComponentType>(l.Type, ignoreCase: true, out var type))
            {
                return Error.Validation($"{l.Component}: type must be Earning, Deduction or EmployerContribution.");
            }

            lines.Add(new SalarySlipLine(l.Component, l.Abbreviation, type, l.Amount, l.Formula));
        }

        var additional = new List<AdditionalPay>();
        foreach (var a in command.Additional ?? [])
        {
            if (!Enum.TryParse<ComponentType>(a.Type, ignoreCase: true, out var type))
            {
                return Error.Validation($"{a.Component}: type must be Earning, Deduction or EmployerContribution.");
            }

            additional.Add(new AdditionalPay(a.Component, a.Abbreviation, type, a.IsTaxable, a.Amount));
        }

        SalarySlip slip;
        try
        {
            slip = SalarySlip.Record(employee.CompanyId, employee.Id, employee.Number, employee.FullName, command.PeriodStart, command.PeriodEnd,
                command.StructureId ?? Guid.Empty, command.StructureName, command.Base, command.PaymentDays, command.TotalWorkingDays, lines, command.TaxableEarnings,
                command.Number ?? string.Empty, additional);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        slip.CreatedAt = clock.GetUtcNow();
        slips.Add(slip);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PayrollMapping.ToDto(slip);
    }
}
