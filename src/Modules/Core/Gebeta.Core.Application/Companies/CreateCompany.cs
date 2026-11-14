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

public sealed class CreateCompanyHandler(ICompanyRepository companies, ICoreUnitOfWork unitOfWork, TimeProvider clock)
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
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return CompanyMapping.ToDto(company);
    }
}

public sealed record ListCompaniesQuery : IQuery<IReadOnlyList<CompanyDto>>;

public sealed class ListCompaniesHandler(ICompanyRepository companies) : IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>>
{
    public async Task<Result<IReadOnlyList<CompanyDto>>> Handle(ListCompaniesQuery query, CancellationToken cancellationToken)
    {
        var all = await companies.ListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<CompanyDto>>(all.Select(CompanyMapping.ToDto).ToList());
    }
}

public sealed record GetCompanyQuery(Guid Id) : IQuery<CompanyDto>;

public sealed class GetCompanyHandler(ICompanyRepository companies) : IQueryHandler<GetCompanyQuery, CompanyDto>
{
    public async Task<Result<CompanyDto>> Handle(GetCompanyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
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
