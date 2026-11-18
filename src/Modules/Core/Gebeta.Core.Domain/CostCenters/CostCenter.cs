using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.CostCenters;

/// <summary>
/// A part of the company that costs and income are reported for: an estate, a factory, a workshop, head office
/// (docs/requirements/sap-features.md #3). Every company has one Main cost centre, made with the company; a profit and loss posting that names
/// none belongs to Main. Codes are unique per company. One level only for now: no tree and no percentage distribution.
/// </summary>
public sealed class CostCenter : Entity, IAudited
{
    public const string MainCode = "MAIN";

    private CostCenter()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }

    /// <summary>The company's default cost centre; it cannot be deactivated.</summary>
    public bool IsMain { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static CostCenter Main(Guid companyId) => new() { CompanyId = companyId, Code = MainCode, Name = "Main", IsMain = true };

    public static Result<CostCenter> Create(Guid companyId, string code, string name)
    {
        var center = new CostCenter { CompanyId = companyId };
        var set = center.Set(code, name, true);
        return set.IsFailure ? set.Error! : center;
    }

    public Result Update(string name, bool isActive)
    {
        if (IsMain && !isActive)
        {
            return Result.Failure(Error.Validation("The Main cost centre takes every posting that names no other; it cannot be deactivated."));
        }

        return Set(Code, name, isActive);
    }

    private Result Set(string code, string name, bool isActive)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 1 or > 20 || !code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
        {
            return Result.Failure(Error.Validation("A cost centre code is 1 to 20 letters, digits, - or _ (e.g. ESTATE)."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 140)
        {
            return Result.Failure(Error.Validation("A cost centre needs a name (at most 140 characters)."));
        }

        (Code, Name, IsActive) = (code, name.Trim(), isActive);
        return Result.Success();
    }
}

public interface ICostCenterRepository
{
    Task<CostCenter?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<CostCenter>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken);
    void Add(CostCenter costCenter);
}
