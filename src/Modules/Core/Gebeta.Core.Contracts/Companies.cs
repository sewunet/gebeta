namespace Gebeta.Core.Contracts;

/// <summary>A company as other modules and the frontend see it.</summary>
public sealed record CompanyDto(
    Guid Id,
    string Name,
    string Abbreviation,
    string LegalForm,
    string Tin,
    string? VatRegistrationNumber,
    string? TradeLicenseNumber,
    string DefaultCurrency,
    string FiscalYearType,
    string TimeZone);

/// <summary>Published when a company is created, so other modules can set up their defaults.</summary>
public sealed record CompanyCreated(Guid CompanyId, string Name, string Abbreviation, string DefaultCurrency, DateTimeOffset OccurredAt);
