using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Employees;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Employees;

/// <summary>Core's document numbers (employee numbers and other Core series).</summary>
public interface ICoreNumbers : IDocumentNumbers;

public sealed record CreateEmployeeCommand(
    Guid CompanyId,
    string FirstName,
    string FatherName,
    string? GrandfatherName,
    Gender Gender,
    DateOnly DateOfJoining,
    string? Tin) : ICommand<EmployeeDto>;

public sealed record ListEmployeesQuery(Guid CompanyId) : IQuery<IReadOnlyList<EmployeeDto>>;

public sealed record GetEmployeeQuery(Guid Id) : IQuery<EmployeeDto>;

public sealed class EmployeeHandler(
    IEmployeeRepository employees,
    ICompanyRepository companies,
    ICompanyAccess access,
    ICoreNumbers numbers,
    ICoreUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreateEmployeeCommand, EmployeeDto>, IQueryHandler<ListEmployeesQuery, IReadOnlyList<EmployeeDto>>, IQueryHandler<GetEmployeeQuery, EmployeeDto>
{
    public const string DocumentType = "Employee";
    public const string DefaultPattern = "EMP-.#####";

    public async Task<Result<EmployeeDto>> Handle(CreateEmployeeCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)
            || await companies.GetAsync(command.CompanyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        // Validate before taking a number, so a refused employee does not use one.
        var check = Employee.Create(command.CompanyId, "CHECK", command.FirstName, command.FatherName, command.GrandfatherName,
            command.Gender, command.DateOfJoining, command.Tin);
        if (check.IsFailure)
        {
            return check.Error!;
        }

        if (check.Value.Tin is { } tin && await employees.TinExistsAsync(command.CompanyId, tin, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"An employee with TIN {tin} already exists in this company.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var number = await numbers.NextAsync(command.CompanyId, DocumentType, DefaultPattern, command.DateOfJoining, cancellationToken).ConfigureAwait(false);
        var employee = Employee.Create(command.CompanyId, number, command.FirstName, command.FatherName, command.GrandfatherName,
            command.Gender, command.DateOfJoining, command.Tin).Value;
        employee.CreatedAt = clock.GetUtcNow();
        employees.Add(employee);
        unitOfWork.Publish(new EmployeeCreated(employee.Id, employee.CompanyId, employee.Number, employee.FullName, employee.DateOfJoining, employee.CreatedAt));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return EmployeeMapping.ToDto(employee);
    }

    public async Task<Result<IReadOnlyList<EmployeeDto>>> Handle(ListEmployeesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var all = await employees.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<EmployeeDto>>(all.Select(EmployeeMapping.ToDto).ToList());
    }

    public async Task<Result<EmployeeDto>> Handle(GetEmployeeQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var employee = await employees.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false)
            ? Error.NotFound("Employee", query.Id)
            : EmployeeMapping.ToDto(employee);
    }
}

/// <summary>Core's employee facts for other modules.</summary>
public sealed class EmployeeDirectory(IEmployeeRepository employees) : IEmployeeDirectory, IEmployeeList
{
    public async Task<IReadOnlyList<EmployeeInfo>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await employees.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).Select(ToInfo).ToList();

    private static EmployeeInfo ToInfo(Employee e) =>
        new(e.Id, e.CompanyId, e.Number, e.FullName, e.Status.ToString(), e.DateOfJoining, e.DateOfLeaving, e.Tin?.Value);

    public async Task<EmployeeInfo?> GetAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var e = await employees.GetAsync(employeeId, cancellationToken).ConfigureAwait(false);
        return e is null ? null : ToInfo(e);
    }
}

internal static class EmployeeMapping
{
    public static EmployeeDto ToDto(Employee e) => new(e.Id, e.CompanyId, e.Number, e.FirstName, e.FatherName, e.GrandfatherName, e.FullName,
        e.Gender.ToString(), e.DateOfJoining, e.DateOfLeaving, e.Status.ToString(), e.Tin?.Value);
}
