using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Application.Employees;
using Gebeta.Core.Application.Security;
using Gebeta.Core.Application.Settings;
using Gebeta.Core.Contracts;
using Gebeta.Modularity;
using Gebeta.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Gebeta.Core.Presentation;

public static class CoreEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The frontend uses this to decide which screens, menu items and slots to show.
        endpoints.MapGet("/api/modules", (ModuleRegistry registry) => TypedResults.Ok(ModulesResponse.From(registry)))
            .WithName("GetModules")
            .WithTags("Core");

        MapMe(endpoints);
        MapCompanies(endpoints);
        MapUsersAndRoles(endpoints);
        MapSettings(endpoints);
        MapEmployees(endpoints);
    }

    private static void MapEmployees(IEndpointRouteBuilder endpoints)
    {
        var employees = endpoints.MapGroup("/api/core/employees").WithTags("Core: Employees");

        employees.MapGet("/", async (Guid companyId, IQueryHandler<ListEmployeesQuery, IReadOnlyList<EmployeeDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListEmployeesQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListEmployees")
            .RequirePermission(CorePermissions.EmployeesRead);

        employees.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetEmployeeQuery, EmployeeDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetEmployeeQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetEmployee")
            .RequirePermission(CorePermissions.EmployeesRead);

        employees.MapPost("/", async (CreateEmployeeCommand command, ICommandHandler<CreateEmployeeCommand, EmployeeDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/core/employees/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("CreateEmployee")
            .RequirePermission(CorePermissions.EmployeesManage);
    }

    private static void MapSettings(IEndpointRouteBuilder endpoints)
    {
        var naming = endpoints.MapGroup("/api/core/companies/{companyId:guid}/naming-series").WithTags("Core: Naming series");

        naming.MapGet("/", async (Guid companyId, IQueryHandler<ListNamingSeriesQuery, IReadOnlyList<NamingSeriesDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListNamingSeriesQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListNamingSeries")
            .RequirePermission(CorePermissions.NamingSeriesRead);

        naming.MapPut("/", async (Guid companyId, SetNamingSeriesBody body, ICommandHandler<SetNamingSeriesCommand, NamingSeriesDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SetNamingSeriesCommand(companyId, body.DocumentType, body.Pattern), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SetNamingSeries")
            .RequirePermission(CorePermissions.NamingSeriesManage);

        var holidays = endpoints.MapGroup("/api/core/holidays").WithTags("Core: Holidays");

        holidays.MapGet("/{ethiopianYear:int}", async (int ethiopianYear, Guid? companyId, IQueryHandler<ListHolidaysQuery, IReadOnlyList<HolidayDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListHolidaysQuery(ethiopianYear, companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListHolidays")
            .RequirePermission(CorePermissions.HolidaysRead);

        holidays.MapPost("/", async (AddHolidayCommand command, ICommandHandler<AddHolidayCommand, HolidayDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/core/holidays", result.Value) : result.ToHttp();
            })
            .WithName("AddHoliday")
            .RequirePermission(CorePermissions.HolidaysManage);
    }

    private static void MapMe(IEndpointRouteBuilder endpoints)
    {
        var me = endpoints.MapGroup("/api/me").WithTags("Core: Me");

        // Any signed-in user: profile, roles and permissions (creates the Gebeta user on first sign-in).
        me.MapGet("/", async (IQueryHandler<GetMeQuery, MeDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetMeQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetMe");

        me.MapPut("/preferences", async (UpdateMyPreferencesCommand command, ICommandHandler<UpdateMyPreferencesCommand, MeDto> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("UpdateMyPreferences");
    }

    private static void MapCompanies(IEndpointRouteBuilder endpoints)
    {
        var companies = endpoints.MapGroup("/api/core/companies").WithTags("Core: Companies");

        companies.MapGet("/", async (IQueryHandler<ListCompaniesQuery, IReadOnlyList<CompanyDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListCompaniesQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListCompanies")
            .RequirePermission(CorePermissions.CompaniesRead);

        companies.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetCompanyQuery, CompanyDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GetCompanyQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetCompany")
            .RequirePermission(CorePermissions.CompaniesRead);

        companies.MapPost("/", async (CreateCompanyCommand command, ICommandHandler<CreateCompanyCommand, CompanyDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess
                    ? Results.Created($"/api/core/companies/{result.Value.Id}", result.Value)
                    : result.ToHttp();
            })
            .WithName("CreateCompany")
            .RequirePermission(CorePermissions.CompaniesCreate);
    }

    private static void MapUsersAndRoles(IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/api/core/users").WithTags("Core: Users");

        users.MapGet("/", async (IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListUsersQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListUsers")
            .RequirePermission(CorePermissions.UsersRead);

        users.MapPut("/{userId:guid}/roles/{roleId:guid}", async (Guid userId, Guid roleId, ICommandHandler<AssignRoleCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(new AssignRoleCommand(userId, roleId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("AssignRole")
            .RequirePermission(CorePermissions.UsersManage);

        users.MapDelete("/{userId:guid}/roles/{roleId:guid}", async (Guid userId, Guid roleId, ICommandHandler<RemoveRoleCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(new RemoveRoleCommand(userId, roleId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("RemoveRole")
            .RequirePermission(CorePermissions.UsersManage);

        users.MapPut("/{userId:guid}/companies/{companyId:guid}", async (Guid userId, Guid companyId, ICommandHandler<GrantCompanyAccessCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(new GrantCompanyAccessCommand(userId, companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GrantCompanyAccess")
            .RequirePermission(CorePermissions.UsersManage);

        users.MapDelete("/{userId:guid}/companies/{companyId:guid}", async (Guid userId, Guid companyId, ICommandHandler<RevokeCompanyAccessCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(new RevokeCompanyAccessCommand(userId, companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("RevokeCompanyAccess")
            .RequirePermission(CorePermissions.UsersManage);

        var roles = endpoints.MapGroup("/api/core/roles").WithTags("Core: Roles");

        roles.MapGet("/", async (IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListRolesQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListRoles")
            .RequirePermission(CorePermissions.RolesRead);

        roles.MapPost("/", async (CreateRoleCommand command, ICommandHandler<CreateRoleCommand, RoleDto> handler, CancellationToken ct) =>
            {
                var result = await handler.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/core/roles/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("CreateRole")
            .RequirePermission(CorePermissions.RolesManage);
    }
}

public sealed record ModulesResponse(IReadOnlyList<ModuleDto> Modules, IReadOnlyList<CapabilityBinding> Capabilities)
{
    public static ModulesResponse From(ModuleRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return new ModulesResponse(
            registry.Modules.Select(m => new ModuleDto(m.Info.Id, m.Info.Name, m.Info.Version.ToString(), m.Enhances)).ToList(),
            registry.Capabilities);
    }
}

/// <summary>Body of PUT naming-series: the document type and its pattern.</summary>
public sealed record SetNamingSeriesBody(string DocumentType, string Pattern);

public sealed record ModuleDto(string Id, string Name, string Version, IReadOnlyList<string> Enhances);
