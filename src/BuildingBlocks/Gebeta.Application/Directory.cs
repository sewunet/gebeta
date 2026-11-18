namespace Gebeta.Application;

/// <summary>The few company facts every module needs (provided by Core).</summary>
public sealed record CompanyInfo(Guid Id, string Name, string Abbreviation, string DefaultCurrency, bool UsesEthiopianFiscalYear, string Tin = "", bool IsVatRegistered = false);

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

/// <summary>Employees of a company, for bulk work such as a payroll run (provided by Core).</summary>
public interface IEmployeeList
{
    Task<IReadOnlyList<EmployeeInfo>> ListAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>A customer or supplier as other modules need it (provided by Core).</summary>
public sealed record PartyInfo(Guid Id, Guid CompanyId, string Name, string Type, string? Tin, bool IsVatRegistered);

public interface IPartyDirectory
{
    Task<PartyInfo?> GetAsync(Guid partyId, CancellationToken cancellationToken);
}

/// <summary>The item facts every module needs (provided by Core).</summary>
public sealed record ItemInfo(Guid Id, Guid CompanyId, string Code, string Name, string Uom, bool IsStockItem, bool IsActive);

public interface IItemDirectory
{
    Task<ItemInfo?> GetAsync(Guid itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemInfo>> ListAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>A cost centre as other modules see it (provided by Core).</summary>
public sealed record CostCenterInfo(Guid Id, Guid CompanyId, string Code, string Name, bool IsMain, bool IsActive);

/// <summary>Cost centres of a company: Accounting checks the ones postings name, Payroll and Cash keep defaults (provided by Core).</summary>
public interface ICostCenterDirectory
{
    Task<CostCenterInfo?> GetAsync(Guid costCenterId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CostCenterInfo>> ListAsync(Guid companyId, CancellationToken cancellationToken);
}

