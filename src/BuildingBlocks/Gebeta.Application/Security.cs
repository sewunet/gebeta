namespace Gebeta.Application;

/// <summary>The person making the current request, as the identity provider (Keycloak) describes them.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>The <c>sub</c> claim: stable id of the user in Keycloak.</summary>
    string? ExternalId { get; }

    string? Username { get; }
    string? Email { get; }
    string? FullName { get; }
}

/// <summary>
/// Answers "may the current user do this?" for a permission such as <c>payroll.salary-slips.submit</c>.
/// Provided by the Core module; every module's endpoints use it through <c>RequirePermission</c>.
/// </summary>
public interface IPermissionChecker
{
    Task<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken);
}

/// <summary>
/// Which companies the current user may work in. Every module filters its records with it, so a
/// clerk of one sugar factory never sees another factory's documents. Provided by the Core module.
/// </summary>
public interface ICompanyAccess
{
    /// <summary>The accessible company ids, or null when the user may access every company.</summary>
    Task<IReadOnlySet<Guid>?> GetAccessibleCompanyIdsAsync(CancellationToken cancellationToken);

    Task<bool> CanAccessAsync(Guid companyId, CancellationToken cancellationToken);
}
