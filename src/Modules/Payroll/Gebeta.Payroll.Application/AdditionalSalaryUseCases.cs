using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Application;

public sealed record AdditionalSalaryLineInput(Guid EmployeeId, decimal Amount, string? Note);

/// <param name="PayrollDate">A day in the payroll month the amounts belong to (the month's slip takes them).</param>
/// <param name="Type">Earning or Deduction.</param>
public sealed record CreateAdditionalSalaryCommand(Guid CompanyId, DateOnly PayrollDate, string Component, string Abbreviation, string Type, bool IsTaxable,
    IReadOnlyList<AdditionalSalaryLineInput> Lines, string? Remark) : ICommand<AdditionalSalaryDto>;

public sealed record SubmitAdditionalSalaryCommand(Guid Id) : ICommand<AdditionalSalaryDto>;

public sealed record CancelAdditionalSalaryCommand(Guid Id) : ICommand<AdditionalSalaryDto>;

public sealed record GetAdditionalSalaryQuery(Guid Id) : IQuery<AdditionalSalaryDto>;

public sealed record ListAdditionalSalariesQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<AdditionalSalaryDto>>;

/// <summary>Additional salary: overtime, allowances and one-off deductions for many employees in one payroll month.</summary>
public sealed class AdditionalSalaryHandler(
    IAdditionalSalaryRepository documents,
    IAdditionalPayRepository entries,
    IEmployeeDirectory employees,
    ICompanyAccess access,
    IPayrollNumbers numbers,
    IPayrollUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreateAdditionalSalaryCommand, AdditionalSalaryDto>,
      ICommandHandler<SubmitAdditionalSalaryCommand, AdditionalSalaryDto>,
      ICommandHandler<CancelAdditionalSalaryCommand, AdditionalSalaryDto>,
      IQueryHandler<GetAdditionalSalaryQuery, AdditionalSalaryDto>,
      IQueryHandler<ListAdditionalSalariesQuery, IReadOnlyList<AdditionalSalaryDto>>
{
    public const string DefaultPattern = "ADS-.FY.-.#####";

    public async Task<Result<AdditionalSalaryDto>> Handle(CreateAdditionalSalaryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        if (!Enum.TryParse<ComponentType>(command.Type, ignoreCase: true, out var type))
        {
            return Error.Validation("The type is Earning or Deduction.");
        }

        var lines = new List<AdditionalSalaryLine>();
        foreach (var line in command.Lines ?? [])
        {
            var employee = await employees.GetAsync(line.EmployeeId, cancellationToken).ConfigureAwait(false);
            if (employee is null || employee.CompanyId != command.CompanyId)
            {
                return Error.Validation($"Employee {line.EmployeeId} is not an employee of this company.");
            }

            lines.Add(new AdditionalSalaryLine(employee.Id, employee.Number, employee.FullName, line.Amount, string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim()));
        }

        AdditionalSalary document;
        try
        {
            document = AdditionalSalary.Create(command.CompanyId, command.PayrollDate, command.Component ?? string.Empty, command.Abbreviation ?? string.Empty, type,
                command.IsTaxable, lines, command.Remark);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        document.AssignName(await numbers.NextAsync(command.CompanyId, AdditionalSalary.Source, DefaultPattern, command.PayrollDate, cancellationToken).ConfigureAwait(false));
        document.CreatedAt = clock.GetUtcNow();
        documents.Add(document);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(document, []);
    }

    public async Task<Result<AdditionalSalaryDto>> Handle(SubmitAdditionalSalaryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } document)
        {
            return Error.NotFound("Additional salary", command.Id);
        }

        IReadOnlyList<AdditionalPayEntry> created;
        try
        {
            created = document.SubmitWithEntries();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        document.ModifiedAt = clock.GetUtcNow();
        entries.Add(created);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(document, created);
    }

    public async Task<Result<AdditionalSalaryDto>> Handle(CancelAdditionalSalaryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } document)
        {
            return Error.NotFound("Additional salary", command.Id);
        }

        var given = await entries.FromSourceAsync(document.Id, cancellationToken).ConfigureAwait(false);
        try
        {
            document.CancelWithEntries(given);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        document.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(document, given);
    }

    public async Task<Result<AdditionalSalaryDto>> Handle(GetAdditionalSalaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await AccessibleAsync(query.Id, cancellationToken).ConfigureAwait(false) is { } document
            ? ToDto(document, await entries.FromSourceAsync(document.Id, cancellationToken).ConfigureAwait(false))
            : Error.NotFound("Additional salary", query.Id);
    }

    public async Task<Result<IReadOnlyList<AdditionalSalaryDto>>> Handle(ListAdditionalSalariesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var list = await documents.ListAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<AdditionalSalaryDto>>(list.Select(d => ToDto(d, null)).ToList());
    }

    private async Task<AdditionalSalary?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return document is not null && await access.CanAccessAsync(document.CompanyId, cancellationToken).ConfigureAwait(false) ? document : null;
    }

    /// <param name="given">The document's entries, to count those already in slips (not loaded for lists).</param>
    private static AdditionalSalaryDto ToDto(AdditionalSalary d, IReadOnlyList<AdditionalPayEntry>? given) =>
        new(d.Id, d.Name, d.CompanyId, d.PayrollDate, d.Component, d.Abbreviation, d.Type.ToString(), d.IsTaxable, d.DocStatus.ToString(), d.Remark,
            d.Lines.Select(l => new AdditionalSalaryLineDto(l.EmployeeId, l.EmployeeNumber, l.EmployeeName, l.Amount, l.Note)).ToList(), d.Total,
            given?.Count(e => e.SalarySlipId is not null) ?? 0);
}
