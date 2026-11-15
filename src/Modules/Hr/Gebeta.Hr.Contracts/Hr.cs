namespace Gebeta.Hr.Contracts;

public static class HrPermissions
{
    public const string LeaveRead = "hr.leave.read";
    public const string LeaveApply = "hr.leave.apply";
    public const string LeaveApprove = "hr.leave.approve";
    public const string LeaveAllocate = "hr.leave.allocate";
    public const string AttendanceRead = "hr.attendance.read";
    public const string AttendanceManage = "hr.attendance.manage";

    public static IReadOnlyList<string> All { get; } = [LeaveRead, LeaveApply, LeaveApprove, LeaveAllocate, AttendanceRead, AttendanceManage];
}

public sealed record LeaveApplicationDto(Guid Id, string? Number, Guid CompanyId, Guid EmployeeId, string EmployeeName, string Type,
    DateOnly FromDate, DateOnly ToDate, decimal Days, bool IsPaid, string? Reason, string Status, string? ApprovedBy);

public sealed record LeaveBalanceDto(string Type, decimal Allocated, decimal Taken, decimal Balance);

public sealed record AllocationResultDto(int FiscalYear, int Allocated, int AlreadyAllocated, decimal TotalDays);

public sealed record AttendanceDto(Guid EmployeeId, DateOnly Date, string Status);

/// <summary>Published when leave is approved (Payroll and others may react).</summary>
public sealed record LeaveApproved(Guid LeaveId, Guid CompanyId, Guid EmployeeId, string Type, DateOnly FromDate, DateOnly ToDate, decimal Days, bool IsPaid,
    DateTimeOffset OccurredAt);
