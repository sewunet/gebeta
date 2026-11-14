using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Settings;
using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;
using Gebeta.SharedKernel.Naming;

namespace Gebeta.Core.Application.Settings;

// ---- Services Core provides to every module ----

public sealed class CompanyDirectory(ICompanyRepository companies) : ICompanyDirectory
{
    public async Task<CompanyInfo?> GetAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var c = await companies.GetAsync(companyId, cancellationToken).ConfigureAwait(false);
        return c is null ? null : new CompanyInfo(c.Id, c.Name, c.Abbreviation, c.DefaultCurrencyCode, c.FiscalYearType == FiscalYearType.Ethiopian);
    }
}

public sealed class NamingSeriesSettings(INamingSeriesRepository series) : INamingSeriesSettings
{
    public async Task<string?> GetPatternAsync(Guid companyId, string documentType, CancellationToken cancellationToken) =>
        (await series.FindAsync(companyId, documentType, cancellationToken).ConfigureAwait(false))?.Pattern;
}

// ---- Naming series ----

public sealed record ListNamingSeriesQuery(Guid CompanyId) : IQuery<IReadOnlyList<NamingSeriesDto>>;

public sealed record SetNamingSeriesCommand(Guid CompanyId, string DocumentType, string Pattern) : ICommand<NamingSeriesDto>;

public sealed class NamingSeriesHandler(
    INamingSeriesRepository series,
    ICompanyRepository companies,
    ICompanyAccess access,
    ICoreUnitOfWork unitOfWork,
    TimeProvider clock)
    : IQueryHandler<ListNamingSeriesQuery, IReadOnlyList<NamingSeriesDto>>, ICommandHandler<SetNamingSeriesCommand, NamingSeriesDto>
{
    public async Task<Result<IReadOnlyList<NamingSeriesDto>>> Handle(ListNamingSeriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var company = await AccessibleCompanyAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var all = await series.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<NamingSeriesDto>>(all.Select(s => ToDto(s, company)).ToList());
    }

    public async Task<Result<NamingSeriesDto>> Handle(SetNamingSeriesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var company = await AccessibleCompanyAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var existing = await series.FindAsync(command.CompanyId, command.DocumentType?.Trim() ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            var created = NamingSeries.Create(command.CompanyId, command.DocumentType ?? string.Empty, command.Pattern);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            existing = created.Value;
            existing.CreatedAt = clock.GetUtcNow();
            series.Add(existing);
        }
        else
        {
            var changed = existing.ChangePattern(command.Pattern);
            if (changed.IsFailure)
            {
                return changed.Error!;
            }

            existing.ModifiedAt = clock.GetUtcNow();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(existing, company);
    }

    private async Task<Company?> AccessibleCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await access.CanAccessAsync(companyId, cancellationToken).ConfigureAwait(false)
            ? await companies.GetAsync(companyId, cancellationToken).ConfigureAwait(false)
            : null;

    private NamingSeriesDto ToDto(NamingSeries s, Company company)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var example = NamingPattern.Parse(s.Pattern).Value.Format(today, company.Abbreviation, 1);
        return new NamingSeriesDto(s.Id, s.CompanyId, s.DocumentType, s.Pattern, example);
    }
}

// ---- Holidays ----

/// <summary>Holidays of an Ethiopian year (Meskerem 1 – Pagume), national plus the company's own when given.</summary>
public sealed record ListHolidaysQuery(int EthiopianYear, Guid? CompanyId) : IQuery<IReadOnlyList<HolidayDto>>;

public sealed record AddHolidayCommand(DateOnly Date, string Name, string? AmharicName, Guid? CompanyId) : ICommand<HolidayDto>;

public sealed class HolidayHandler(IHolidayRepository holidays, ICompanyAccess access, ICoreUnitOfWork unitOfWork, TimeProvider clock)
    : IQueryHandler<ListHolidaysQuery, IReadOnlyList<HolidayDto>>, ICommandHandler<AddHolidayCommand, HolidayDto>
{
    public async Task<Result<IReadOnlyList<HolidayDto>>> Handle(ListHolidaysQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.EthiopianYear is < 1900 or > 2091)
        {
            return Error.Validation("The Ethiopian year must be between 1900 and 2091.");
        }

        if (query.CompanyId is { } companyId && !await access.CanAccessAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", companyId);
        }

        var from = new EthiopianDate(query.EthiopianYear, 1, 1).ToGregorian();
        var to = new EthiopianDate(query.EthiopianYear, 13, EthiopianDate.DaysInMonth(query.EthiopianYear, 13)).ToGregorian();

        var computed = EthiopianHolidays.ForEthiopianYear(query.EthiopianYear)
            .Select(h => ToDto(h.Date, h.Name, h.AmharicName, h.Kind.ToString(), null));
        var stored = (await holidays.ListAsync(from, to, query.CompanyId, cancellationToken).ConfigureAwait(false))
            .Select(h => ToDto(h.Date, h.Name, h.AmharicName, h.CompanyId is null ? nameof(HolidayKind.Announced) : "Company", h.CompanyId));

        return Result.Success<IReadOnlyList<HolidayDto>>(computed.Concat(stored).OrderBy(h => h.Date).ToList());
    }

    public async Task<Result<HolidayDto>> Handle(AddHolidayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CompanyId is { } companyId && !await access.CanAccessAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", companyId);
        }

        var created = Holiday.Create(command.Date, command.Name, command.AmharicName, command.CompanyId);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var holiday = created.Value;
        if (await holidays.ExistsAsync(holiday.Date, holiday.Name, holiday.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"{holiday.Name} on {holiday.Date:yyyy-MM-dd} is already in the calendar.");
        }

        holiday.CreatedAt = clock.GetUtcNow();
        holidays.Add(holiday);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(holiday.Date, holiday.Name, holiday.AmharicName, holiday.CompanyId is null ? nameof(HolidayKind.Announced) : "Company", holiday.CompanyId);
    }

    private static HolidayDto ToDto(DateOnly date, string name, string? amharic, string kind, Guid? companyId) =>
        new(date, EthiopianDate.FromGregorian(date).ToString(), name, amharic, kind, companyId);
}
