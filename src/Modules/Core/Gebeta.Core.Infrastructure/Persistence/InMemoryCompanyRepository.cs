using System.Collections.Concurrent;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Domain.Companies;

namespace Gebeta.Core.Infrastructure.Persistence;

/// <summary>
/// Temporary storage so the layers can be built and tested end to end.
/// Replaced by EF Core + PostgreSQL (schema "core") in the next step.
/// </summary>
internal sealed class InMemoryCoreStore
{
    public ConcurrentDictionary<Guid, Company> Companies { get; } = new();
}

internal sealed class InMemoryCompanyRepository(InMemoryCoreStore store, InMemoryCoreUnitOfWork unitOfWork) : ICompanyRepository
{
    public Task<Company?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Companies.GetValueOrDefault(id));

    public Task<bool> ExistsAsync(string abbreviation, string tin, CancellationToken cancellationToken) =>
        Task.FromResult(store.Companies.Values.Concat(unitOfWork.PendingCompanies)
            .Any(c => c.Abbreviation == abbreviation || c.Tin.Value == tin));

    public Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Company>>(store.Companies.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList());

    public void Add(Company company) => unitOfWork.PendingCompanies.Add(company);
}

/// <summary>Keeps added companies until SaveChanges, like a real unit of work.</summary>
internal sealed class InMemoryCoreUnitOfWork(InMemoryCoreStore store) : ICoreUnitOfWork
{
    public List<Company> PendingCompanies { get; } = [];

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var company in PendingCompanies)
        {
            store.Companies[company.Id] = company;
        }

        PendingCompanies.Clear();
        return Task.CompletedTask;
    }
}
