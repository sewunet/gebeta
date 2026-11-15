namespace Gebeta.Application;

/// <summary>The few company facts every module needs (provided by Core).</summary>
public sealed record CompanyInfo(Guid Id, string Name, string Abbreviation, string DefaultCurrency, bool UsesEthiopianFiscalYear);

public interface ICompanyDirectory
{
    Task<CompanyInfo?> GetAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>
/// The document number pattern configured for a company and document type, e.g. "PCR-.FY.-.#####".
/// Null when nothing is configured; the module then uses its own default pattern. Provided by Core.
/// </summary>
public interface INamingSeriesSettings
{
    Task<string?> GetPatternAsync(Guid companyId, string documentType, CancellationToken cancellationToken);
}

/// <summary>The employee facts every module needs (provided by Core).</summary>
public sealed record EmployeeInfo(
    Guid Id,
    Guid CompanyId,
    string Number,
    string FullName,
    string Status,
    DateOnly DateOfJoining,
    DateOnly? DateOfLeaving,
    string? Tin);

public interface IEmployeeDirectory
{
    Task<EmployeeInfo?> GetAsync(Guid employeeId, CancellationToken cancellationToken);
}
