using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Companies;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Companies;

/// <summary>Core's unit of work, so each module saves only its own changes.</summary>
public interface ICoreUnitOfWork : IUnitOfWork;

public sealed record CreateCompanyCommand(
    string Name,
    string Abbreviation,
    LegalForm LegalForm,
    string Tin,
    string? VatRegistrationNumber,
    string? TradeLicenseNumber) : ICommand<CompanyDto>;

public sealed class CreateCompanyHandler(ICompanyRepository companies, ICoreUnitOfWork unitOfWork, Security.UserContext userContext, TimeProvider clock)
    : ICommandHandler<CreateCompanyCommand, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(CreateCompanyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var created = Company.Create(command.Name, command.Abbreviation, command.LegalForm, command.Tin,
            command.VatRegistrationNumber, command.TradeLicenseNumber);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var company = created.Value;
        if (await companies.ExistsAsync(company.Abbreviation, company.Tin.Value, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"A company with abbreviation {company.Abbreviation} or TIN {company.Tin} already exists.");
        }

        company.CreatedAt = clock.GetUtcNow();
        companies.Add(company);

        // The creator can always work in the company they created.
        if (await userContext.GetAsync(cancellationToken).ConfigureAwait(false) is { } creator)
        {
            company.CreatedBy = creator.User.Username;
            creator.User.GrantCompany(company.Id);
        }

        // Other modules (Accounting, Payroll, ...) set up their defaults for the new company.
        unitOfWork.Publish(new CompanyCreated(company.Id, company.Name, company.Abbreviation, company.DefaultCurrencyCode, company.CreatedAt));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CompanyMapping.ToDto(company);
    }
}

public sealed record ListCompaniesQuery : IQuery<IReadOnlyList<CompanyDto>>;

public sealed class ListCompaniesHandler(ICompanyRepository companies, ICompanyAccess access) : IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>>
{
    public async Task<Result<IReadOnlyList<CompanyDto>>> Handle(ListCompaniesQuery query, CancellationToken cancellationToken)
    {
        var accessible = await access.GetAccessibleCompanyIdsAsync(cancellationToken).ConfigureAwait(false);
        var all = await companies.ListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<CompanyDto>>(all
            .Where(c => accessible is null || accessible.Contains(c.Id))
            .Select(CompanyMapping.ToDto)
            .ToList());
    }
}

public sealed record GetCompanyQuery(Guid Id) : IQuery<CompanyDto>;

public sealed class GetCompanyHandler(ICompanyRepository companies, ICompanyAccess access) : IQueryHandler<GetCompanyQuery, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(GetCompanyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // A company the user may not access is reported as not found, so its existence is not revealed.
        if (!await access.CanAccessAsync(query.Id, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.Id);
        }

        var company = await companies.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return company is null ? Error.NotFound("Company", query.Id) : CompanyMapping.ToDto(company);
    }
}

internal static class CompanyMapping
{
    public static CompanyDto ToDto(Company c) => new(
        c.Id, c.Name, c.Abbreviation, c.LegalForm.ToString(), c.Tin.Value, c.VatRegistrationNumber, c.TradeLicenseNumber,
        c.DefaultCurrencyCode, c.FiscalYearType.ToString(), c.TimeZone);
}

public sealed record UpdateCompanyCommand(Guid Id, string Name, string? VatRegistrationNumber, string? TradeLicenseNumber) : ICommand<CompanyDto>;

public sealed class UpdateCompanyHandler(ICompanyRepository companies, ICompanyAccess access, ICoreUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdateCompanyCommand, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(UpdateCompanyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var company = await access.CanAccessAsync(command.Id, cancellationToken).ConfigureAwait(false)
            ? await companies.GetAsync(command.Id, cancellationToken).ConfigureAwait(false)
            : null;
        if (company is null)
        {
            return Error.NotFound("Company", command.Id);
        }

        var updated = company.Update(command.Name, command.VatRegistrationNumber, command.TradeLicenseNumber);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        company.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CompanyMapping.ToDto(company);
    }
}
