namespace Gebeta.Core.Contracts;

/// <summary>Permissions defined by the Core module.</summary>
public static class CorePermissions
{
    public const string CompaniesRead = "core.companies.read";
    public const string CompaniesCreate = "core.companies.create";
    public const string UsersRead = "core.users.read";
    public const string UsersManage = "core.users.manage";
    public const string RolesRead = "core.roles.read";
    public const string RolesManage = "core.roles.manage";

    public static IReadOnlyList<string> All { get; } =
        [CompaniesRead, CompaniesCreate, UsersRead, UsersManage, RolesRead, RolesManage];
}
