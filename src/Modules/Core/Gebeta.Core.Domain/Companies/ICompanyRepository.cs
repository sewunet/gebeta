namespace Gebeta.Core.Domain.Companies;

/// <summary>Storage for companies. Implemented in Infrastructure.</summary>
public interface ICompanyRepository
{
    Task<Company?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(string abbreviation, string tin, CancellationToken cancellationToken);
    Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken);
    void Add(Company company);
}
