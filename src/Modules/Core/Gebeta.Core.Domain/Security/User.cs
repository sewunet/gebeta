using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Security;

/// <summary>
/// A person who signs in. The identity (password, MFA) lives in Keycloak; Gebeta keeps the profile,
/// preferences and role assignments, linked by the Keycloak subject id (docs/platform.md).
/// </summary>
public sealed class User : Entity
{
    private readonly List<Guid> _roleIds = [];

    private User()
    {
        ExternalId = string.Empty;
        Username = string.Empty;
    }

    private User(string externalId, string username)
    {
        ExternalId = externalId;
        Username = username;
    }

    /// <summary>The <c>sub</c> claim from Keycloak.</summary>
    public string ExternalId { get; private set; }

    public string Username { get; private set; }
    public string? Email { get; private set; }
    public string? FullName { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>UI language: "en" or "am" (Amharic).</summary>
    public string Language { get; private set; } = "en";

    public CalendarPreference Calendar { get; private set; } = CalendarPreference.Both;
    public Guid? DefaultCompanyId { get; private set; }
    public IReadOnlyList<Guid> RoleIds => _roleIds;

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
