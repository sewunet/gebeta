using Gebeta.Application;
using Gebeta.Hr.Contracts;
using Gebeta.Hr.Domain.Leave;
using Gebeta.SharedKernel;

namespace Gebeta.Hr.Application;

public sealed record MyLeaveQuery : IQuery<MyLeaveDto>;

public sealed record ApplyMyLeaveCommand(LeaveType Type, DateOnly FromDate, DateOnly ToDate, string? Reason) : ICommand<LeaveApplicationDto>;

/// <summary>
/// Employee self-service: the signed-in employee's own leave. The employee always comes from the signed-in user (Core), never from
/// the request, so nobody can see or apply for someone else.
/// </summary>
public sealed class HrSelfServiceHandler(ICurrentEmployee me, ILeaveRepository leave, LeaveHandler applications)
    : IQueryHandler<MyLeaveQuery, MyLeaveDto>, ICommandHandler<ApplyMyLeaveCommand, LeaveApplicationDto>
{
    internal static readonly Error NotAnEmployee = Error.Validation("Your user is not linked to an employee record; ask HR to link it.");

    public async Task<Result<MyLeaveDto>> Handle(MyLeaveQuery query, CancellationToken cancellationToken)
    {
        if (await me.EmployeeIdAsync(cancellationToken).ConfigureAwait(false) is not { } employeeId)
        {
            return NotAnEmployee;
        }

        var balances = await applications.Handle(new LeaveBalanceQuery(employeeId), cancellationToken).ConfigureAwait(false);
        if (balances.IsFailure)
        {
            return balances.Error!;
        }

        var mine = await leave.ForEmployeeAsync(employeeId, cancellationToken).ConfigureAwait(false);
        return new MyLeaveDto(balances.Value, mine.Select(LeaveHandler.ToDto).ToList());
    }

    public async Task<Result<LeaveApplicationDto>> Handle(ApplyMyLeaveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return await me.EmployeeIdAsync(cancellationToken).ConfigureAwait(false) is { } employeeId
            ? await applications.Handle(new ApplyLeaveCommand(employeeId, command.Type, command.FromDate, command.ToDate, command.Reason), cancellationToken).ConfigureAwait(false)
            : NotAnEmployee;
    }
}
