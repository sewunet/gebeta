using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Core.Domain.Employees;

public enum EmployeeStatus
{
    Active,
    Suspended,
    Left,
}

public enum Gender
{
    Female,
    Male,
}

/// <summary>
/// The shared employee record every module can rely on (docs/plugin-architecture.md: Core keeps a small
/// directory, modules add their own details). Names follow Ethiopian practice: given name, father's name,
/// grandfather's name. HR adds personal data and history; Payroll adds the salary profile.
/// </summary>
public sealed class Employee : Entity, IAudited
{
    private Employee()
    {
        Number = string.Empty;
        FirstName = string.Empty;
        FatherName = string.Empty;
    }

    private Employee(Guid companyId, string number, string firstName, string fatherName, string? grandfatherName, Gender gender, DateOnly dateOfJoining)
    {
        CompanyId = companyId;
        Number = number;
        FirstName = firstName;
        FatherName = fatherName;
        GrandfatherName = grandfatherName;
        Gender = gender;
        DateOfJoining = dateOfJoining;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Employee number from the company's naming series, e.g. EMP-00012.</summary>
    public string Number { get; private set; }

    public string FirstName { get; private set; }
    public string FatherName { get; private set; }
    public string? GrandfatherName { get; private set; }
    public Gender Gender { get; private set; }
    public DateOnly DateOfJoining { get; private set; }
    public DateOnly? DateOfLeaving { get; private set; }
    public EmployeeStatus Status { get; private set; } = EmployeeStatus.Active;
    public TaxpayerIdentificationNumber? Tin { get; private set; }

    public string FullName => string.Join(' ', new[] { FirstName, FatherName, GrandfatherName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    public static Result<Employee> Create(Guid companyId, string number, string firstName, string fatherName, string? grandfatherName,
        Gender gender, DateOnly dateOfJoining, string? tin)
    {
        if (companyId == Guid.Empty)
        {
            return Error.Validation("An employee must belong to a company.");
        }

        if (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(fatherName))
        {
            return Error.Validation("The given name and the father's name are required.");
        }

        if (new[] { firstName, fatherName, grandfatherName ?? string.Empty }.Any(n => n.Trim().Length > 60))
        {
            return Error.Validation("Each name may have at most 60 characters.");
        }

        TaxpayerIdentificationNumber? parsedTin = null;
        if (!string.IsNullOrWhiteSpace(tin))
        {
            var result = TaxpayerIdentificationNumber.Create(tin);
            if (result.IsFailure)
            {
                return result.Error!;
            }

            parsedTin = result.Value;
        }

        return new Employee(companyId, number, firstName.Trim(), fatherName.Trim(),
            string.IsNullOrWhiteSpace(grandfatherName) ? null : grandfatherName.Trim(), gender, dateOfJoining)
        {
            Tin = parsedTin,
        };
    }

    /// <summary>Corrects the employee's names, gender, joining date or TIN (the number stays).</summary>
    public Result Update(string firstName, string fatherName, string? grandfatherName, Gender gender, DateOnly dateOfJoining, string? tin)
    {
        var check = Create(CompanyId, Number, firstName, fatherName, grandfatherName, gender, dateOfJoining, tin);
        if (check.IsFailure)
        {
            return Result.Failure(check.Error!);
        }

        if (DateOfLeaving is { } left && dateOfJoining > left)
        {
            return Result.Failure(Error.Validation("The joining date cannot be after the leaving date."));
        }

        var c = check.Value;
        (FirstName, FatherName, GrandfatherName, Gender, DateOfJoining, Tin) = (c.FirstName, c.FatherName, c.GrandfatherName, c.Gender, c.DateOfJoining, c.Tin);
        return Result.Success();
    }

    /// <summary>Takes back a leaving recorded by mistake.</summary>
    public Result Rejoin()
    {
        if (Status != EmployeeStatus.Left)
        {
            return Result.Failure(Error.Validation("Only an employee who has left can be taken back."));
        }

        DateOfLeaving = null;
        Status = EmployeeStatus.Active;
        return Result.Success();
    }

    public Result Leave(DateOnly dateOfLeaving)
    {
        if (Status == EmployeeStatus.Left)
        {
            return Result.Failure(Error.Validation("This employee has already left."));
        }

        if (dateOfLeaving < DateOfJoining)
        {
            return Result.Failure(Error.Validation("The leaving date cannot be before the joining date."));
        }

        DateOfLeaving = dateOfLeaving;
        Status = EmployeeStatus.Left;
        return Result.Success();
    }

    /// <summary>True when the employee works for the company at some time between the two dates.</summary>
    public bool IsEmployedBetween(DateOnly from, DateOnly to) =>
        DateOfJoining <= to && (DateOfLeaving is null || DateOfLeaving >= from);
}

public interface IEmployeeRepository
{
    Task<Employee?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Employee>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> TinExistsAsync(Guid companyId, TaxpayerIdentificationNumber tin, CancellationToken cancellationToken);
    void Add(Employee employee);
}
