using Gebeta.SharedKernel;

namespace Gebeta.Hr.Domain.Leave;

/// <summary>
/// A leave request. Draft when applied for; approving submits it and writes it to the leave ledger;
/// cancelling an approved leave writes a reversal.
/// </summary>
public sealed class LeaveApplication : DocumentBase
{
    private LeaveApplication()
    {
        EmployeeName = string.Empty;
    }

    private LeaveApplication(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        EmployeeName = string.Empty;
    }

    public override string DocumentType => "Leave Application";

    public Guid EmployeeId { get; private set; }
    public string EmployeeName { get; private set; }
    public LeaveType Type { get; private set; }
    public DateOnly FromDate { get; private set; }
    public DateOnly ToDate { get; private set; }
    public decimal Days { get; private set; }
    public string? Reason { get; private set; }
    public string? ApprovedBy { get; private set; }

    public static LeaveApplication Create(Guid companyId, Guid employeeId, string employeeName, LeaveType type, DateOnly from, DateOnly to,
        string? reason, DateOnly today)
    {
        if (to < from)
        {
            throw new DomainException("hr.leave_dates", "The leave cannot end before it starts.");
        }

        var rule = EthiopianLeaveRules.Rules[type];
        var days = EthiopianLeaveRules.CountDays(type, from, to);
        if (days <= 0)
        {
            throw new DomainException("hr.leave_no_days", "The chosen dates contain no working days (Sundays and public holidays are not counted).");
        }

        if (rule.MaximumDays is { } max && days > max)
        {
            throw new DomainException("hr.leave_too_long", $"{type} leave is at most {max} days ({rule.LegalBasis}).");
        }

        return new LeaveApplication(companyId, today)
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            Type = type,
            FromDate = from,
            ToDate = to,
            Days = days,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
    }

    public bool Overlaps(DateOnly from, DateOnly to) => FromDate <= to && ToDate >= from;

    /// <summary>Approves (submits) the leave; for leave that uses a balance, <paramref name="balance"/> must cover it.</summary>
    public LeaveLedgerEntry Approve(string approvedBy, decimal balance)
    {
        if (EthiopianLeaveRules.Rules[Type].UsesBalance && balance < Days)
        {
            throw new DomainException("hr.leave_balance", $"{EmployeeName} has {balance} days of {Type} leave left; {Days} requested.");
        }

        Submit();
        ApprovedBy = approvedBy;
        return LeaveLedgerEntry.Create(CompanyId, EmployeeId, Type, FromDate, -Days, LeaveLedgerSource.Application, Id);
    }

    /// <summary>Cancels an approved leave and returns the reversal for the ledger.</summary>
    public LeaveLedgerEntry CancelApproved()
    {
        Cancel();
        return LeaveLedgerEntry.Create(CompanyId, EmployeeId, Type, FromDate, Days, LeaveLedgerSource.Cancellation, Id);
    }
}

public enum LeaveLedgerSource
{
    Allocation,
    Application,
    Cancellation,
}

/// <summary>Append-only leave ledger (ADR 0004): allocations add days, approved leave takes them.</summary>
public sealed class LeaveLedgerEntry : Entity
{
    private LeaveLedgerEntry()
    {
    }

    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public LeaveType Type { get; private set; }
    public DateOnly Date { get; private set; }
    public decimal Days { get; private set; }
    public LeaveLedgerSource Source { get; private set; }
    public Guid? SourceId { get; private set; }

    /// <summary>For allocations: the Ethiopian fiscal year the days belong to.</summary>
    public int? FiscalYear { get; private set; }

    public static LeaveLedgerEntry Create(Guid companyId, Guid employeeId, LeaveType type, DateOnly date, decimal days, LeaveLedgerSource source,
        Guid? sourceId, int? fiscalYear = null) =>
        new()
        {
            CompanyId = companyId,
            EmployeeId = employeeId,
            Type = type,
            Date = date,
            Days = days,
            Source = source,
            SourceId = sourceId,
            FiscalYear = fiscalYear,
        };
}

public enum AttendanceStatus
{
    Present,
    Absent,
    HalfDay,
}

/// <summary>An attendance exception for one day (days without a record count as present).</summary>
public sealed class AttendanceRecord : Entity
{
    private AttendanceRecord()
    {
    }

    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly Date { get; private set; }
    public AttendanceStatus Status { get; private set; }

    public static AttendanceRecord Create(Guid companyId, Guid employeeId, DateOnly date, AttendanceStatus status) =>
        new() { CompanyId = companyId, EmployeeId = employeeId, Date = date, Status = status };

    public void Change(AttendanceStatus status) => Status = status;

    /// <summary>Unpaid share of the day: absent 1, half day 0.5, present 0.</summary>
    public decimal UnpaidDays => Status switch
    {
        AttendanceStatus.Absent => 1m,
        AttendanceStatus.HalfDay => 0.5m,
        _ => 0m,
    };
}

public interface ILeaveRepository
{
    Task<LeaveApplication?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<LeaveApplication>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Approved leave of the employee overlapping the dates.</summary>
    Task<IReadOnlyList<LeaveApplication>> ApprovedAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Draft or approved leave of the employee overlapping the dates.</summary>
    Task<bool> HasOverlapAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    Task<decimal> BalanceAsync(Guid employeeId, LeaveType type, CancellationToken cancellationToken);
    Task<IReadOnlyList<LeaveLedgerEntry>> LedgerAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<bool> AllocatedAsync(Guid employeeId, LeaveType type, int fiscalYear, CancellationToken cancellationToken);
    void Add(LeaveApplication application);
    void Add(LeaveLedgerEntry entry);
}

public interface IAttendanceRepository
{
    Task<AttendanceRecord?> GetAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<AttendanceRecord>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<AttendanceRecord>> ForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    void Add(AttendanceRecord record);
}
