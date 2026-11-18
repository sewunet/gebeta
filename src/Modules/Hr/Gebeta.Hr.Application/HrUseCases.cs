using Gebeta.Application;
using Gebeta.Hr.Contracts;
using Gebeta.Hr.Domain.Leave;
using Gebeta.Payroll.Contracts;
using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Hr.Application;

public interface IHrUnitOfWork : IUnitOfWork;

public interface IHrNumbers : IDocumentNumbers;

// ---------------------------------------------------------------- leave

public sealed record ApplyLeaveCommand(Guid EmployeeId, LeaveType Type, DateOnly FromDate, DateOnly ToDate, string? Reason) : ICommand<LeaveApplicationDto>;

public sealed record ApproveLeaveCommand(Guid LeaveId) : ICommand<LeaveApplicationDto>;

public sealed record CancelLeaveCommand(Guid LeaveId) : ICommand<LeaveApplicationDto>;

public sealed record ListLeaveQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<LeaveApplicationDto>>;

public sealed record LeaveBalanceQuery(Guid EmployeeId) : IQuery<IReadOnlyList<LeaveBalanceDto>>;

public sealed record AllocateAnnualLeaveCommand(Guid CompanyId, int FiscalYear) : ICommand<AllocationResultDto>;

/// <summary>
/// The leave balance an employee brings from the previous system at cut-over: one opening entry per leave type that has a balance
/// (annual leave). <paramref name="IncludesThisYear"/>: the previous system already gave this Ethiopian fiscal year's allocation, so
/// the yearly allocation must not give it again.
/// </summary>
public sealed record RecordOpeningLeaveCommand(Guid EmployeeId, LeaveType Type, DateOnly Date, decimal Days, bool IncludesThisYear) : ICommand<LeaveBalanceDto>;

public sealed class LeaveHandler(
    ILeaveRepository leave,
    IEmployeeDirectory employees,
    IEmployeeList employeeList,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IHrNumbers numbers,
    IHrUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<ApplyLeaveCommand, LeaveApplicationDto>,
      ICommandHandler<ApproveLeaveCommand, LeaveApplicationDto>,
      ICommandHandler<CancelLeaveCommand, LeaveApplicationDto>,
      IQueryHandler<ListLeaveQuery, IReadOnlyList<LeaveApplicationDto>>,
      IQueryHandler<LeaveBalanceQuery, IReadOnlyList<LeaveBalanceDto>>,
      ICommandHandler<AllocateAnnualLeaveCommand, AllocationResultDto>,
      ICommandHandler<RecordOpeningLeaveCommand, LeaveBalanceDto>
{
    public const string DefaultPattern = "LV-.FY.-.#####";

    public async Task<Result<LeaveApplicationDto>> Handle(ApplyLeaveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        if (await leave.HasOverlapAsync(employee.Id, command.FromDate, command.ToDate, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"{employee.FullName} already has leave in these dates.");
        }

        LeaveApplication application;
        try
        {
            application = LeaveApplication.Create(employee.CompanyId, employee.Id, employee.FullName, command.Type, command.FromDate, command.ToDate,
                command.Reason, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        application.AssignName(await numbers.NextAsync(employee.CompanyId, application.DocumentType, DefaultPattern, command.FromDate, cancellationToken).ConfigureAwait(false));
        application.CreatedAt = clock.GetUtcNow();
        application.CreatedBy = currentUser.Username;
        leave.Add(application);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(application);
    }

    public async Task<Result<LeaveApplicationDto>> Handle(ApproveLeaveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var application = await AccessibleAsync(command.LeaveId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return Error.NotFound("Leave application", command.LeaveId);
        }

        try
        {
            var balance = await leave.BalanceAsync(application.EmployeeId, application.Type, cancellationToken).ConfigureAwait(false);
            leave.Add(application.Approve(currentUser.Username ?? "unknown", balance));
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        application.ModifiedAt = clock.GetUtcNow();
        unitOfWork.Publish(new LeaveApproved(application.Id, application.CompanyId, application.EmployeeId, application.Type.ToString(),
            application.FromDate, application.ToDate, application.Days, EthiopianLeaveRules.Rules[application.Type].IsPaid, application.ModifiedAt.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(application);
    }

    public async Task<Result<LeaveApplicationDto>> Handle(CancelLeaveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var application = await AccessibleAsync(command.LeaveId, cancellationToken).ConfigureAwait(false);
        if (application is null)
        {
            return Error.NotFound("Leave application", command.LeaveId);
        }

        try
        {
            leave.Add(application.CancelApproved());
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        application.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(application);
    }

    public async Task<Result<IReadOnlyList<LeaveApplicationDto>>> Handle(ListLeaveQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<LeaveApplicationDto>>(
            (await leave.ListAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<LeaveBalanceDto>>> Handle(LeaveBalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var employee = await employees.GetAsync(query.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", query.EmployeeId);
        }

        var ledger = await leave.LedgerAsync(employee.Id, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<LeaveBalanceDto>>(ledger.GroupBy(e => e.Type).OrderBy(g => g.Key).Select(g =>
        {
            var allocated = g.Where(e => e.Source is LeaveLedgerSource.Allocation or LeaveLedgerSource.Opening).Sum(e => e.Days);
            var taken = -g.Where(e => e.Source is not (LeaveLedgerSource.Allocation or LeaveLedgerSource.Opening)).Sum(e => e.Days);
            // Only annual leave has a balance; for other types the days taken are what matters.
            var balance = EthiopianLeaveRules.Rules[g.Key].UsesBalance ? allocated - taken : 0m;
            return new LeaveBalanceDto(g.Key.ToString(), allocated, taken, balance);
        }).ToList());
    }

    /// <summary>Gives every active employee their annual leave for an Ethiopian fiscal year (once per employee and year).</summary>
    public async Task<Result<AllocationResultDto>> Handle(AllocateAnnualLeaveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var fiscalYear = new EthiopianFiscalYear(command.FiscalYear);
        int allocated = 0, already = 0;
        var total = 0m;
        foreach (var employee in await employeeList.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            if (employee.DateOfJoining > fiscalYear.EndDate || employee.DateOfLeaving < fiscalYear.StartDate)
            {
                continue;
            }

            if (await leave.AllocatedAsync(employee.Id, LeaveType.Annual, command.FiscalYear, cancellationToken).ConfigureAwait(false))
            {
                already++;
                continue;
            }

            var days = EthiopianLeaveRules.AnnualEntitlement(employee.DateOfJoining, fiscalYear.StartDate);
            if (days <= 0)
            {
                continue;
            }

            leave.Add(LeaveLedgerEntry.Create(command.CompanyId, employee.Id, LeaveType.Annual, fiscalYear.StartDate, days,
                LeaveLedgerSource.Allocation, null, command.FiscalYear));
            allocated++;
            total += days;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AllocationResultDto(command.FiscalYear, allocated, already, total);
    }

    public async Task<Result<LeaveBalanceDto>> Handle(RecordOpeningLeaveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        if (!EthiopianLeaveRules.Rules[command.Type].UsesBalance)
        {
            return Error.Validation($"{command.Type} leave has no balance to bring over; only the days taken count, against the legal maximum.");
        }

        if (command.Days < 0m || decimal.Round(command.Days, 1) != command.Days)
        {
            return Error.Validation("An opening leave balance is zero or more days, in steps of half a day at most.");
        }

        var ledger = await leave.LedgerAsync(employee.Id, cancellationToken).ConfigureAwait(false);
        if (ledger.Any(e => e.Type == command.Type && e.Source == LeaveLedgerSource.Opening))
        {
            return Error.Conflict($"{employee.FullName} already has an opening {command.Type} leave balance.");
        }

        int? fiscalYear = command.IncludesThisYear ? EthiopianFiscalYear.Containing(command.Date).Year : null;
        leave.Add(LeaveLedgerEntry.Create(employee.CompanyId, employee.Id, command.Type, command.Date, command.Days, LeaveLedgerSource.Opening, null, fiscalYear));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var balances = await Handle(new LeaveBalanceQuery(employee.Id), cancellationToken).ConfigureAwait(false);
        return balances.IsFailure ? balances.Error! : balances.Value.Single(b => b.Type == command.Type.ToString());
    }

    private async Task<LeaveApplication?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var application = await leave.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return application is not null && await access.CanAccessAsync(application.CompanyId, cancellationToken).ConfigureAwait(false) ? application : null;
    }

    internal static LeaveApplicationDto ToDto(LeaveApplication a) => new(a.Id, a.Name, a.CompanyId, a.EmployeeId, a.EmployeeName, a.Type.ToString(),
        a.FromDate, a.ToDate, a.Days, EthiopianLeaveRules.Rules[a.Type].IsPaid, a.Reason, a.DocStatus.ToString(), a.ApprovedBy);
}

// ---------------------------------------------------------------- attendance

public sealed record MarkAttendanceCommand(Guid EmployeeId, DateOnly Date, AttendanceStatus Status) : ICommand<AttendanceDto>;

public sealed record ListAttendanceQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AttendanceDto>>;

public sealed class AttendanceHandler(IAttendanceRepository attendance, IEmployeeDirectory employees, ICompanyAccess access, IHrUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<MarkAttendanceCommand, AttendanceDto>, IQueryHandler<ListAttendanceQuery, IReadOnlyList<AttendanceDto>>
{
    public async Task<Result<AttendanceDto>> Handle(MarkAttendanceCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        if (command.Date < employee.DateOfJoining || command.Date > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(31))
        {
            return Error.Validation("Attendance can only be recorded from the joining date and not far in the future.");
        }

        var record = await attendance.GetAsync(employee.Id, command.Date, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            record = AttendanceRecord.Create(employee.CompanyId, employee.Id, command.Date, command.Status);
            record.CreatedAt = clock.GetUtcNow();
            attendance.Add(record);
        }
        else
        {
            record.Change(command.Status);
            record.ModifiedAt = clock.GetUtcNow();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new AttendanceDto(record.EmployeeId, record.Date, record.Status.ToString());
    }

    public async Task<Result<IReadOnlyList<AttendanceDto>>> Handle(ListAttendanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<AttendanceDto>>((await attendance.ListAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false))
            .Select(r => new AttendanceDto(r.EmployeeId, r.Date, r.Status.ToString())).ToList());
    }
}

// ---------------------------------------------------------------- capability for Payroll

/// <summary>
/// HR's payment days for Payroll (docs/plugin-architecture.md): days employed in the month on the 30-day basis,
/// minus unpaid leave, absences and half days, and the unpaid share of sick leave beyond the first month
/// (Labour Proclamation 1156/2019, Art. 85). Days already covered by approved leave are not counted twice.
/// </summary>
public sealed class HrPaymentDays(ILeaveRepository leave, IAttendanceRepository attendance, IEmployeeDirectory employees) : IPaymentDaysProvider
{
    public async Task<decimal?> GetPaymentDaysAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, decimal totalWorkingDays, CancellationToken cancellationToken)
    {
        var employee = await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null)
        {
            return null;
        }

        var from = employee.DateOfJoining > periodStart ? employee.DateOfJoining : periodStart;
        var to = employee.DateOfLeaving is { } left && left < periodEnd ? left : periodEnd;
        if (to < from)
        {
            return 0m;
        }

        var employed = from == periodStart && to == periodEnd ? totalWorkingDays : Math.Min(totalWorkingDays, to.DayNumber - from.DayNumber + 1);

        var approved = await leave.ApprovedAsync(employeeId, periodEnd.AddDays(-365), periodEnd, cancellationToken).ConfigureAwait(false);
        var unpaid = 0m;
        var onLeave = new HashSet<DateOnly>();

        foreach (var application in approved.Where(a => a.Overlaps(from, to)))
        {
            for (var d = Max(application.FromDate, from); d <= Min(application.ToDate, to); d = d.AddDays(1))
            {
                onLeave.Add(d);
                if (application.Type == LeaveType.Unpaid)
                {
                    unpaid += 1m;
                }
            }
        }

        // Sick leave: the pay of each sick day depends on how many sick days came before it in 12 months.
        var sickDays = approved.Where(a => a.Type == LeaveType.Sick)
            .SelectMany(a => Enumerable.Range(0, a.ToDate.DayNumber - a.FromDate.DayNumber + 1).Select(i => a.FromDate.AddDays(i)))
            .Distinct().Order().ToList();
        foreach (var day in sickDays.Where(d => d >= from && d <= to))
        {
            var number = sickDays.Count(s => s <= day && s > day.AddDays(-365));
            unpaid += 1m - EthiopianLeaveRules.SickPayFactor(number);
        }

        foreach (var record in await attendance.ForEmployeeAsync(employeeId, from, to, cancellationToken).ConfigureAwait(false))
        {
            if (!onLeave.Contains(record.Date))
            {
                unpaid += record.UnpaidDays;
            }
        }

        return Math.Max(0m, employed - unpaid);
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
