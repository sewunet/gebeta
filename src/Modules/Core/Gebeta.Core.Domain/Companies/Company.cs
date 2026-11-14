using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Core.Domain.Companies;

/// <summary>
/// A legal entity that keeps its own books. Defaults follow Ethiopian practice: birr (ETB),
/// the Hamle–Sene fiscal year and the Addis Ababa time zone (docs/ethiopia.md).
/// </summary>
public sealed class Company : Entity
{
    public const string DefaultCurrency = "ETB";
    public const string DefaultTimeZone = "Africa/Addis_Ababa";

    private Company(string name, string abbreviation, LegalForm legalForm, TaxpayerIdentificationNumber tin)
    {
        Name = name;
        Abbreviation = abbreviation;
        LegalForm = legalForm;
        Tin = tin;
    }

    public string Name { get; private set; }

    /// <summary>Short code used in account and document names, e.g. ESIG in "Cash - ESIG".</summary>
    public string Abbreviation { get; private set; }

    public LegalForm LegalForm { get; private set; }
    public TaxpayerIdentificationNumber Tin { get; private set; }

    /// <summary>VAT registration number; empty when the company is not VAT-registered (then it may owe turnover tax).</summary>
    public string? VatRegistrationNumber { get; private set; }

    public string? TradeLicenseNumber { get; private set; }
    public string DefaultCurrencyCode { get; private set; } = DefaultCurrency;
    public FiscalYearType FiscalYearType { get; private set; } = FiscalYearType.Ethiopian;
    public string TimeZone { get; private set; } = DefaultTimeZone;

    public bool IsVatRegistered => VatRegistrationNumber is not null;

    public static Result<Company> Create(string name, string abbreviation, LegalForm legalForm, string tin,
        string? vatRegistrationNumber = null, string? tradeLicenseNumber = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("The company name is required.");
        }

        var abbr = (abbreviation ?? string.Empty).Trim().ToUpperInvariant();
        if (abbr.Length is < 2 or > 10 || !abbr.All(char.IsAsciiLetterOrDigit))
        {
            return Error.Validation("The abbreviation must be 2 to 10 letters or digits.");
        }

        var parsedTin = TaxpayerIdentificationNumber.Create(tin);
        if (parsedTin.IsFailure)
        {
            return parsedTin.Error!;
        }

        var company = new Company(name.Trim(), abbr, legalForm, parsedTin.Value)
        {
            VatRegistrationNumber = string.IsNullOrWhiteSpace(vatRegistrationNumber) ? null : vatRegistrationNumber.Trim(),
            TradeLicenseNumber = string.IsNullOrWhiteSpace(tradeLicenseNumber) ? null : tradeLicenseNumber.Trim(),
        };
        return company;
    }

    /// <summary>The fiscal year that contains <paramref name="date"/>, as Gregorian start and end dates.</summary>
    public (DateOnly Start, DateOnly End) FiscalYearOf(DateOnly date)
    {
        if (FiscalYearType == FiscalYearType.Gregorian)
        {
            return (new DateOnly(date.Year, 1, 1), new DateOnly(date.Year, 12, 31));
        }

        var fy = EthiopianFiscalYear.Containing(date);
        return (fy.StartDate, fy.EndDate);
    }

    public void UseGregorianFiscalYear() => FiscalYearType = FiscalYearType.Gregorian;
}

/// <summary>Business forms under the Commercial Code (Proclamation 1243/2021) and other Ethiopian laws.</summary>
public enum LegalForm
{
    SoleProprietorship,
    GeneralPartnership,
    LimitedPartnership,
    JointVenture,
    PrivateLimitedCompany,
    OnePersonPrivateLimitedCompany,
    ShareCompany,
    PublicEnterprise,
    Cooperative,
    CivilSocietyOrganization,
    GovernmentBody,
    ForeignBranch,
}

public enum FiscalYearType
{
    /// <summary>Hamle 1 – Sene 30, the Ethiopian government fiscal year.</summary>
    Ethiopian,

    /// <summary>1 January – 31 December, for example for foreign-owned companies reporting to a parent.</summary>
    Gregorian,
}
