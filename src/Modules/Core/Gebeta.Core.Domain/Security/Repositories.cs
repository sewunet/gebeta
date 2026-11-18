namespace Gebeta.Core.Domain.Security;

public interface IUserRepository
{
    Task<User?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<User?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken);

    /// <summary>An invited user (not signed in yet) with this username.</summary>
    Task<User?> GetInvitedAsync(string username, CancellationToken cancellationToken);

    Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken);
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken);
    void Add(User user);

    /// <summary>
    /// Saves a user signing in for the first time. If a parallel request (the web app loads several things at once on
    /// first sign-in) created the same user a moment earlier, returns that user instead of failing.
    /// </summary>
    Task<User> AddOrGetExistingAsync(User user, CancellationToken cancellationToken);
}

public interface IRoleRepository
{
    Task<Role?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Role>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    void Add(Role role);
}
