namespace Gebeta.Core.Contracts;

public sealed record EmployeeDto(
    Guid Id,
    Guid CompanyId,
    string Number,
    string FirstName,
    string FatherName,
    string? GrandfatherName,
    string FullName,
    string Gender,
    DateOnly DateOfJoining,
    DateOnly? DateOfLeaving,
    string Status,
    string? Tin);

/// <summary>Published when an employee is added, so Payroll, HR and others can set up their records.</summary>
public sealed record EmployeeCreated(Guid EmployeeId, Guid CompanyId, string Number, string FullName, DateOnly DateOfJoining, DateTimeOffset OccurredAt);
