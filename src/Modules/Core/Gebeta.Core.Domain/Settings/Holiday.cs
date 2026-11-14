using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Settings;

/// <summary>
/// A holiday entered by hand: an announced Islamic holiday (Eid al-Fitr, Eid al-Adha, Mawlid) for all
/// companies, or a local holiday for one company (e.g. a regional holiday). Computed national holidays
/// (Enkutatash, Meskel, Genna, Fasika, ...) are not stored.
/// </summary>
public sealed class Holiday : Entity
{
    private Holiday()
    {
        Name = string.Empty;
    }

    private Holiday(DateOnly date, string name, string? amharicName, Guid? companyId)
    {
        Date = date;
        Name = name;
        AmharicName = amharicName;
        CompanyId = companyId;
    }

    public DateOnly Date { get; private set; }
    public string Name { get; private set; }
    public string? AmharicName { get; private set; }

    /// <summary>Null for a national holiday (all companies).</summary>
    public Guid? CompanyId { get; private set; }

    public static Result<Holiday> Create(DateOnly date, string name, string? amharicName, Guid? companyId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            return Error.Validation("A holiday name is required (at most 120 characters).");
        }

        return new Holiday(date, name.Trim(), string.IsNullOrWhiteSpace(amharicName) ? null : amharicName.Trim(), companyId);
    }
}

public interface IHolidayRepository
{
    /// <summary>National holidays plus those of <paramref name="companyId"/>, between the two dates.</summary>
    Task<IReadOnlyList<Holiday>> ListAsync(DateOnly from, DateOnly to, Guid? companyId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(DateOnly date, string name, Guid? companyId, CancellationToken cancellationToken);
    void Add(Holiday holiday);
}
