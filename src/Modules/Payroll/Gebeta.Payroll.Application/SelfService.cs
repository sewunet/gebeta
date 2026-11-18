using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Application;

public sealed record MySalarySlipsQuery : IQuery<IReadOnlyList<SalarySlipDto>>;

public sealed record MySalarySlipQuery(Guid SlipId) : IQuery<SalarySlipDto>;

/// <summary>
/// Employee self-service: the signed-in employee's own submitted payslips (history slips from before the cut-over included). Drafts
/// stay with payroll until submitted. The employee always comes from the signed-in user, never from the request.
/// </summary>
public sealed class PayrollSelfServiceHandler(ICurrentEmployee me, ISalarySlipRepository slips)
    : IQueryHandler<MySalarySlipsQuery, IReadOnlyList<SalarySlipDto>>, IQueryHandler<MySalarySlipQuery, SalarySlipDto>
{
    private static readonly Error NotAnEmployee = Error.Validation("Your user is not linked to an employee record; ask HR to link it.");

    public async Task<Result<IReadOnlyList<SalarySlipDto>>> Handle(MySalarySlipsQuery query, CancellationToken cancellationToken)
    {
        if (await me.EmployeeIdAsync(cancellationToken).ConfigureAwait(false) is not { } employeeId)
        {
            return NotAnEmployee;
        }

        var mine = await slips.SubmittedForEmployeeAsync(employeeId, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<SalarySlipDto>>(mine.OrderByDescending(s => s.PeriodStart).Select(PayrollMapping.ToDto).ToList());
    }

    public async Task<Result<SalarySlipDto>> Handle(MySalarySlipQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await me.EmployeeIdAsync(cancellationToken).ConfigureAwait(false) is not { } employeeId)
        {
            return NotAnEmployee;
        }

        var slip = await slips.GetAsync(query.SlipId, cancellationToken).ConfigureAwait(false);
        return slip is not null && slip.EmployeeId == employeeId && slip.DocStatus == DocStatus.Submitted
            ? PayrollMapping.ToDto(slip)
            : Error.NotFound("Salary slip", query.SlipId); // someone else's, or not submitted: as if it did not exist
    }
}
