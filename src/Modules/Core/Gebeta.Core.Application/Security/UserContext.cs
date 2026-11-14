using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Domain.Security;

namespace Gebeta.Core.Application.Security;

/// <summary>Security settings from configuration (<c>Security</c> section).</summary>
public sealed class SecurityOptions
{
    public const string Section = "Security";

    /// <summary>
    /// Usernames that receive the System Manager role the first time they sign in. Used to set up a
    /// new installation; remove them once real administrators are assigned.
    /// </summary>
    public IList<string> BootstrapAdmins { get; } = [];
}

/// <summary>
/// The Gebeta user behind the current request, with roles and permissions, loaded once per request.
/// Users are created on their first sign-in from the Keycloak token (just-in-time provisioning).
/// </summary>
public sealed class UserContext(
    ICurrentUser currentUser,
    IUserRepository users,
    IRoleRepository roles,
    ICoreUnitOfWork unitOfWork,
    SecurityOptions options,
    TimeProvider clock) : IPermissionChecker
{
    private (User User, IReadOnlyList<Role> Roles)? _loaded;

    public async Task<(User User, IReadOnlyList<Role> Roles)?> GetAsync(CancellationToken cancellationToken)
    {
        if (_loaded is not null)
        {
            return _loaded;
        }

        if (!currentUser.IsAuthenticated || string.IsNullOrWhiteSpace(currentUser.ExternalId))
        {
            return null;
        }

        var user = await users.GetByExternalIdAsync(currentUser.ExternalId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            user = await ProvisionAsync(cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                return null;
            }
        }
        else if (user.Email != currentUser.Email || user.FullName != currentUser.FullName)
        {
            user.UpdateProfile(currentUser.Email, currentUser.FullName);
            user.ModifiedAt = clock.GetUtcNow();
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!user.IsActive)
        {
            return null;
        }

        var userRoles = await roles.GetManyAsync(user.RoleIds.ToList(), cancellationToken).ConfigureAwait(false);
        _loaded = (user, userRoles);
        return _loaded;
    }

    public async Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
    {
        var loaded = await GetAsync(cancellationToken).ConfigureAwait(false);
        return loaded is { } l && l.Roles.Any(r => r.Allows(permission));
    }

    private async Task<User?> ProvisionAsync(CancellationToken cancellationToken)
    {
        var created = User.Create(currentUser.ExternalId!, currentUser.Username ?? currentUser.ExternalId!, currentUser.Email, currentUser.FullName);
        if (created.IsFailure)
        {
            return null;
        }

        var user = created.Value;
        user.CreatedAt = clock.GetUtcNow();
        user.CreatedBy = user.Username;

        if (options.BootstrapAdmins.Contains(user.Username, StringComparer.OrdinalIgnoreCase)
            && await roles.GetByNameAsync(Role.SystemManager, cancellationToken).ConfigureAwait(false) is { } admin)
        {
            user.AssignRole(admin);
        }

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return user;
    }
}
