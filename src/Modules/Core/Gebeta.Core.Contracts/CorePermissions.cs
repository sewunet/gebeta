namespace Gebeta.Core.Contracts;

/// <summary>Permissions defined by the Core module.</summary>
public static class CorePermissions
{
    public const string CompaniesRead = "core.companies.read";
    public const string CompaniesCreate = "core.companies.create";

    /// <summary>See and use every company, not only the ones assigned to the user.</summary>
    public const string CompaniesAccessAll = "core.companies.access-all";
    public const string UsersRead = "core.users.read";
    public const string UsersManage = "core.users.manage";
    public const string RolesRead = "core.roles.read";
    public const string RolesManage = "core.roles.manage";
    public const string NamingSeriesRead = "core.naming-series.read";
    public const string NamingSeriesManage = "core.naming-series.manage";
    public const string HolidaysRead = "core.holidays.read";
    public const string HolidaysManage = "core.holidays.manage";

    public static IReadOnlyList<string> All { get; } =
        [CompaniesRead, CompaniesCreate, CompaniesAccessAll, UsersRead, UsersManage, RolesRead, RolesManage, NamingSeriesRead, NamingSeriesManage, HolidaysRead, HolidaysManage];
}
