using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Security;

/// <summary>
/// A person who signs in. The identity (password, MFA) lives in Keycloak; Gebeta keeps the profile,
/// preferences and role assignments, linked by the Keycloak subject id (docs/platform.md).
/// </summary>
public sealed class User : Entity, IAudited
{
    private readonly List<Guid> _roleIds = [];
    private readonly List<Guid> _companyIds = [];

    private User()
    {
        Username = string.Empty;
    }

    private User(string? externalId, string username)
    {
        ExternalId = externalId;
        Username = username;
    }

    /// <summary>The <c>sub</c> claim from Keycloak; empty for an invited user until their first sign-in.</summary>
    public string? ExternalId { get; private set; }

    /// <summary>The employee this user is, for self-service (their own payslips and leave); empty for users who are not employees.</summary>
    public Guid? EmployeeId { get; private set; }

    public void LinkEmployee(Guid? employeeId) => EmployeeId = employeeId;

    /// <summary>Invited (roles and companies set up ahead) but not signed in yet.</summary>
    public bool IsInvited => ExternalId is null;

    public string Username { get; private set; }
    public string? Email { get; private set; }
    public string? FullName { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>UI language: "en" or "am" (Amharic).</summary>
    public string Language { get; private set; } = "en";

    public CalendarPreference Calendar { get; private set; } = CalendarPreference.Both;
    public Guid? DefaultCompanyId { get; private set; }
    public IReadOnlyList<Guid> RoleIds => _roleIds;

    /// <summary>
    /// Companies this user may work in (record restriction, docs/platform.md). Users whose roles grant
    /// <c>core.companies.access-all</c> see every company regardless of this list.
    /// </summary>
    public IReadOnlyList<Guid> CompanyIds => _companyIds;

    public static Result<User> Create(string externalId, string username, string? email, string? fullName)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return Error.Validation("The identity provider subject is required.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            return Error.Validation("A username is required.");
        }

        var user = new User(externalId.Trim(), username.Trim().ToLowerInvariant());
        user.UpdateProfile(email, fullName);
        return user;
    }

    /// <summary>
    /// A user set up before their first sign-in (an administrator inviting them, or a data migration), so their roles and companies
    /// are ready. The first sign-in with the same username and email links the Keycloak identity (see <see cref="LinkSignIn"/>).
    /// </summary>
    public static Result<User> Invite(string username, string? email, string? fullName)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return Error.Validation("A username is required.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
        {
            return Error.Validation("An invited user needs an email address; their first sign-in must match it.");
        }

        var user = new User(null, username.Trim().ToLowerInvariant());
        user.UpdateProfile(email, fullName);
        return user;
    }

    /// <summary>
    /// The invited user's first sign-in. Only the same username and the same email link the identity: the username alone could be taken
    /// by someone else in a realm that lets people register.
    /// </summary>
    public bool LinkSignIn(string externalId, string? username, string? email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalId);
        if (!IsInvited || !string.Equals(Username, username?.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Email, email?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ExternalId = externalId.Trim();
        return true;
    }

    /// <summary>Copies name and email from the identity provider on each sign-in.</summary>
    public void UpdateProfile(string? email, string? fullName)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
    }

    public Result SetPreferences(string language, CalendarPreference calendar, Guid? defaultCompanyId)
    {
        if (language is not ("en" or "am"))
        {
            return Result.Failure(Error.Validation("Language must be 'en' (English) or 'am' (Amharic)."));
        }

        Language = language;
        Calendar = calendar;
        DefaultCompanyId = defaultCompanyId;
        return Result.Success();
    }

    public void GrantCompany(Guid companyId)
    {
        if (companyId != Guid.Empty && !_companyIds.Contains(companyId))
        {
            _companyIds.Add(companyId);
        }
    }

    public void RevokeCompany(Guid companyId)
    {
        _companyIds.Remove(companyId);
        if (DefaultCompanyId == companyId)
        {
            DefaultCompanyId = null;
        }
    }

    public void AssignRole(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (!_roleIds.Contains(role.Id))
        {
            _roleIds.Add(role.Id);
        }
    }

    public void RemoveRole(Guid roleId) => _roleIds.Remove(roleId);

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}

/// <summary>How dates are shown to the user. Storage is always Gregorian.</summary>
public enum CalendarPreference
{
    Ethiopian,
    Gregorian,
    Both,
}
