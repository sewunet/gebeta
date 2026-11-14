namespace Gebeta.Core.Contracts;

/// <summary>The signed-in user: profile, preferences, roles and effective permissions. Drives the frontend.</summary>
public sealed record MeDto(
    Guid Id,
    string Username,
    string? Email,
    string? FullName,
    string Language,
    string Calendar,
    Guid? DefaultCompanyId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool AccessAllCompanies,
    IReadOnlyList<Guid> CompanyIds);

public sealed record UserDto(Guid Id, string Username, string? Email, string? FullName, bool IsActive, IReadOnlyList<string> Roles, IReadOnlyList<Guid> CompanyIds);

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);
