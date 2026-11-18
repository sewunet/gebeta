namespace Gebeta.Core.Contracts;

/// <summary>Permissions defined by the Core module.</summary>
public static class CorePermissions
{
    public const string CompaniesRead = "core.companies.read";
    public const string CompaniesCreate = "core.companies.create";
    public const string CompaniesManage = "core.companies.manage";

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
    public const string EmployeesRead = "core.employees.read";
    public const string EmployeesManage = "core.employees.manage";
    public const string PartiesRead = "core.parties.read";
    public const string PartiesManage = "core.parties.manage";
    public const string ItemsRead = "core.items.read";
    public const string ItemsManage = "core.items.manage";
    public const string CostCentersRead = "core.cost-centers.read";
    public const string CostCentersManage = "core.cost-centers.manage";

    /// <summary>The change history of any record (who changed what, when); checked by the platform endpoint /api/history.</summary>
    public const string HistoryRead = "core.history.read";

    public static IReadOnlyList<string> All { get; } =
        [CompaniesRead, CompaniesCreate, CompaniesManage, CompaniesAccessAll, UsersRead, UsersManage, RolesRead, RolesManage, NamingSeriesRead, NamingSeriesManage, HolidaysRead, HolidaysManage, EmployeesRead, EmployeesManage, PartiesRead, PartiesManage, ItemsRead, ItemsManage, CostCentersRead, CostCentersManage, HistoryRead];
}
