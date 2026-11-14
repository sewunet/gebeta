using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Security;

/// <summary>
/// A named set of permissions (docs/platform.md). Permissions look like
/// <c>module.resource.action</c>, for example <c>core.companies.create</c>. A role may hold
/// wildcards: <c>*</c> (everything) or <c>core.*</c> / <c>core.companies.*</c>.
/// </summary>
public sealed class Role : Entity
{
    public const string SystemManager = "System Manager";

    private readonly List<string> _permissions = [];

    private Role()
    {
        Name = string.Empty;
    }

    private Role(string name, string? description, bool isSystem)
    {
        Name = name;
        Description = description;
        IsSystem = isSystem;
    }

    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>System roles are created by Gebeta and cannot be deleted or renamed.</summary>
    public bool IsSystem { get; private set; }

    public IReadOnlyList<string> Permissions => _permissions;

    public static Result<Role> Create(string name, string? description = null, bool isSystem = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            return Error.Validation("A role name is required (at most 80 characters).");
        }

        return new Role(name.Trim(), description?.Trim(), isSystem);
    }

    public Result Grant(string permission)
    {
        if (!PermissionPattern.IsValidPattern(permission))
        {
            return Result.Failure(Error.Validation($"'{permission}' is not a valid permission (module.resource.action, or a wildcard)."));
        }

        var normalized = permission.Trim().ToLowerInvariant();
        if (!_permissions.Contains(normalized, StringComparer.Ordinal))
        {
            _permissions.Add(normalized);
        }

        return Result.Success();
    }

    public void Revoke(string permission) =>
        _permissions.RemoveAll(p => string.Equals(p, permission?.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool Allows(string permission) => _permissions.Any(granted => PermissionPattern.Matches(granted, permission));
}

public static class PermissionPattern
{
    /// <summary>True when <paramref name="granted"/> (possibly a wildcard) covers <paramref name="required"/>.</summary>
    public static bool Matches(string granted, string required)
    {
        ArgumentNullException.ThrowIfNull(granted);
        ArgumentNullException.ThrowIfNull(required);

        if (granted == "*")
        {
            return true;
        }

        if (granted.EndsWith(".*", StringComparison.Ordinal))
        {
            return required.StartsWith(granted[..^1], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(granted, required, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>module.resource.action (lower-case letters, digits, hyphens), or "*", or a prefix ending in ".*".</summary>
    public static bool IsValidPattern(string? permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        var value = permission.Trim().ToLowerInvariant();
        if (value == "*")
        {
            return true;
        }

        var parts = value.Split('.');
        var isWildcard = parts[^1] == "*";
        var named = isWildcard ? parts[..^1] : parts;
        return (isWildcard ? named.Length is >= 1 and <= 2 : named.Length == 3)
            && named.All(p => p.Length > 0 && p.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-'));
    }
}
