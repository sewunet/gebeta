namespace Gebeta.Core.Domain.Security;

public interface IUserRepository
{
    Task<User?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken);
    void Add(User user);
}

public interface IRoleRepository
{
    Task<Role?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Role>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    void Add(Role role);
}
