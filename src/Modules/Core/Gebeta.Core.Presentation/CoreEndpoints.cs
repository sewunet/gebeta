using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Application.Employees;
using Gebeta.Core.Application.Parties;
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
        MapItems(endpoints);
        MapCostCenters(endpoints);
        MapAttachments(endpoints);
        MapNotifications(endpoints);
        MapAlerts(endpoints);
        MapEmployees(endpoints);

        var parties = endpoints.MapGroup("/api/core/parties").WithTags("Core: Customers and suppliers");
        parties.MapGet("/", async (Guid companyId, IQueryHandler<Gebeta.Core.Application.Parties.ListPartiesQuery, IReadOnlyList<PartyDto>> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Parties.ListPartiesQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListParties").RequirePermission(CorePermissions.PartiesRead);
        parties.MapPost("/", async (Gebeta.Core.Application.Parties.CreatePartyCommand command, ICommandHandler<Gebeta.Core.Application.Parties.CreatePartyCommand, PartyDto> h, CancellationToken ct) =>
            {
                var result = await h.Handle(command, ct).ConfigureAwait(false);
                return result.IsSuccess ? Results.Created($"/api/core/parties/{result.Value.Id}", result.Value) : result.ToHttp();
            })
            .WithName("CreateParty").RequirePermission(CorePermissions.PartiesManage);
        parties.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetPartyQuery, PartyDto> h, CancellationToken ct) =>
                (await h.Handle(new GetPartyQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetParty").RequirePermission(CorePermissions.PartiesRead);
        parties.MapPut("/{id:guid}", async (Guid id, PartyBody b, ICommandHandler<UpdatePartyCommand, PartyDto> h, CancellationToken ct) =>
                (await h.Handle(new UpdatePartyCommand(id, b.Name, b.Type, b.Tin, b.VatRegistrationNumber, b.Phone), ct).ConfigureAwait(false)).ToHttp())
            .WithName("UpdateParty").RequirePermission(CorePermissions.PartiesManage);
    }

    private static void MapItems(IEndpointRouteBuilder endpoints)
    {
        var items = endpoints.MapGroup("/api/core/items").WithTags("Core: Items");
        items.MapGet("/", async (Guid companyId, IQueryHandler<Gebeta.Core.Application.Items.ListItemsQuery, IReadOnlyList<ItemDto>> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Items.ListItemsQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListItems").RequirePermission(CorePermissions.ItemsRead);
        items.MapGet("/{id:guid}", async (Guid id, IQueryHandler<Gebeta.Core.Application.Items.GetItemQuery, ItemDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Items.GetItemQuery(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("GetItem").RequirePermission(CorePermissions.ItemsRead);
        items.MapPost("/", async (Gebeta.Core.Application.Items.CreateItemCommand command, ICommandHandler<Gebeta.Core.Application.Items.CreateItemCommand, ItemDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateItem").RequirePermission(CorePermissions.ItemsManage);
        items.MapPut("/{id:guid}", async (Guid id, ItemBody b, ICommandHandler<Gebeta.Core.Application.Items.UpdateItemCommand, ItemDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Items.UpdateItemCommand(id, b.Name, b.Uom, b.Group, b.IsStockItem, b.IsActive), ct).ConfigureAwait(false)).ToHttp())
            .WithName("UpdateItem").RequirePermission(CorePermissions.ItemsManage);
    }

    private static void MapCostCenters(IEndpointRouteBuilder endpoints)
    {
        var centers = endpoints.MapGroup("/api/core/cost-centers").WithTags("Core: Cost centres");
        centers.MapGet("/", async (Guid companyId, IQueryHandler<Gebeta.Core.Application.CostCenters.ListCostCentersQuery, IReadOnlyList<CostCenterDto>> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.CostCenters.ListCostCentersQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListCostCenters").RequirePermission(CorePermissions.CostCentersRead);
        centers.MapPost("/", async (Gebeta.Core.Application.CostCenters.CreateCostCenterCommand command,
                ICommandHandler<Gebeta.Core.Application.CostCenters.CreateCostCenterCommand, CostCenterDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateCostCenter").RequirePermission(CorePermissions.CostCentersManage);
        centers.MapPut("/{id:guid}", async (Guid id, CostCenterBody b, ICommandHandler<Gebeta.Core.Application.CostCenters.UpdateCostCenterCommand, CostCenterDto> h,
                CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.CostCenters.UpdateCostCenterCommand(id, b.Name, b.IsActive), ct).ConfigureAwait(false)).ToHttp())
            .WithName("UpdateCostCenter").RequirePermission(CorePermissions.CostCentersManage);
    }

    /// <summary>Alert rules (docs/requirements/sap-features.md #9). The schedule runs them daily; "run" does it now.</summary>
    private static void MapAlerts(IEndpointRouteBuilder endpoints)
    {
        var alerts = endpoints.MapGroup("/api/core/alerts").WithTags("Core: Alerts");
        alerts.MapGet("/conditions", async (IQueryHandler<Gebeta.Core.Application.Alerts.ListAlertConditionsQuery, IReadOnlyList<AlertConditionDto>> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Alerts.ListAlertConditionsQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListAlertConditions").RequirePermission(CorePermissions.AlertsRead);
        alerts.MapGet("/rules", async (Guid companyId, IQueryHandler<Gebeta.Core.Application.Alerts.ListAlertRulesQuery, IReadOnlyList<AlertRuleDto>> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Alerts.ListAlertRulesQuery(companyId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListAlertRules").RequirePermission(CorePermissions.AlertsRead);
        alerts.MapPost("/rules", async (Gebeta.Core.Application.Alerts.CreateAlertRuleCommand command, ICommandHandler<Gebeta.Core.Application.Alerts.CreateAlertRuleCommand, AlertRuleDto> h, CancellationToken ct) =>
                (await h.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("CreateAlertRule").RequirePermission(CorePermissions.AlertsManage);
        alerts.MapPut("/rules/{id:guid}", async (Guid id, AlertRuleBody r, ICommandHandler<Gebeta.Core.Application.Alerts.UpdateAlertRuleCommand, AlertRuleDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Alerts.UpdateAlertRuleCommand(id, r.Name, r.Threshold, r.UserNames, r.RoleNames), ct).ConfigureAwait(false)).ToHttp())
            .WithName("UpdateAlertRule").RequirePermission(CorePermissions.AlertsManage);
        alerts.MapPost("/rules/{id:guid}/enable", async (Guid id, ICommandHandler<Gebeta.Core.Application.Alerts.SetAlertRuleEnabledCommand, AlertRuleDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Alerts.SetAlertRuleEnabledCommand(id, true), ct).ConfigureAwait(false)).ToHttp())
            .WithName("EnableAlertRule").RequirePermission(CorePermissions.AlertsManage);
        alerts.MapPost("/rules/{id:guid}/disable", async (Guid id, ICommandHandler<Gebeta.Core.Application.Alerts.SetAlertRuleEnabledCommand, AlertRuleDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Alerts.SetAlertRuleEnabledCommand(id, false), ct).ConfigureAwait(false)).ToHttp())
            .WithName("DisableAlertRule").RequirePermission(CorePermissions.AlertsManage);
        alerts.MapDelete("/rules/{id:guid}", async (Guid id, ICommandHandler<Gebeta.Core.Application.Alerts.DeleteAlertRuleCommand, AlertRuleDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Alerts.DeleteAlertRuleCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("DeleteAlertRule").RequirePermission(CorePermissions.AlertsManage);
        alerts.MapPost("/rules/{id:guid}/run", async (Guid id, ICommandHandler<Gebeta.Core.Application.Alerts.RunAlertRuleCommand, AlertRunDto> h, CancellationToken ct, DateOnly? asOf = null) =>
                (await h.Handle(new Gebeta.Core.Application.Alerts.RunAlertRuleCommand(id, asOf), ct).ConfigureAwait(false)).ToHttp())
            .WithName("RunAlertRule").RequirePermission(CorePermissions.AlertsManage);
    }

    /// <summary>The signed-in user's own inbox. No permission is needed: every call is about the caller's own notifications, and nobody else's.</summary>
    private static void MapNotifications(IEndpointRouteBuilder endpoints)
    {
        var inbox = endpoints.MapGroup("/api/notifications").WithTags("Core: Notifications");
        inbox.MapGet("/", async (IQueryHandler<Gebeta.Core.Application.Notifications.ListMyNotificationsQuery, NotificationPageDto> h, CancellationToken ct, bool unread = false, int page = 1, int pageSize = 20) =>
                (await h.Handle(new Gebeta.Core.Application.Notifications.ListMyNotificationsQuery(unread, page, pageSize), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListMyNotifications");
        inbox.MapGet("/unread-count", async (IQueryHandler<Gebeta.Core.Application.Notifications.CountMyUnreadQuery, UnreadCountDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Notifications.CountMyUnreadQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("CountMyUnreadNotifications");
        inbox.MapPost("/{id:guid}/read", async (Guid id, ICommandHandler<Gebeta.Core.Application.Notifications.MarkNotificationReadCommand, NotificationDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Notifications.MarkNotificationReadCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("MarkNotificationRead");
        inbox.MapPost("/read-all", async (ICommandHandler<Gebeta.Core.Application.Notifications.MarkAllNotificationsReadCommand, UnreadCountDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Notifications.MarkAllNotificationsReadCommand(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("MarkAllNotificationsRead");
    }

    private static void MapAttachments(IEndpointRouteBuilder endpoints)
    {
        var files = endpoints.MapGroup("/api/attachments").WithTags("Core: Attachments");
        files.MapGet("/", async (Guid companyId, Guid entityId, IQueryHandler<Gebeta.Core.Application.Attachments.ListAttachmentsQuery, IReadOnlyList<AttachmentDto>> h,
                CancellationToken ct) => (await h.Handle(new Gebeta.Core.Application.Attachments.ListAttachmentsQuery(companyId, entityId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListAttachments").RequirePermission(CorePermissions.AttachmentsRead);

        // A form upload (companyId, entityId, file). Bearer tokens, not cookies, so there is no cross-site form to guard against.
        files.MapPost("/", async (HttpRequest request, ICommandHandler<Gebeta.Core.Application.Attachments.AddAttachmentCommand, AttachmentDto> h, CancellationToken ct) =>
            {
                if (!request.HasFormContentType)
                {
                    return Results.Problem(title: "Send the file as a form upload (multipart/form-data).", statusCode: StatusCodes.Status400BadRequest);
                }

                var form = await request.ReadFormAsync(ct).ConfigureAwait(false);
                if (form.Files.Count != 1 || !Guid.TryParse(form["companyId"], out var companyId) || !Guid.TryParse(form["entityId"], out var entityId))
                {
                    return Results.Problem(title: "Send one file with the companyId and entityId it belongs to.", statusCode: StatusCodes.Status400BadRequest);
                }

                var file = form.Files[0];
                if (file.Length > Gebeta.Core.Domain.Attachments.Attachment.MaxBytes)
                {
                    return Results.Problem(title: "A file is at most 10 MB.", statusCode: StatusCodes.Status400BadRequest);
                }

                using var buffer = new MemoryStream();
                await file.CopyToAsync(buffer, ct).ConfigureAwait(false);
                return (await h.Handle(new Gebeta.Core.Application.Attachments.AddAttachmentCommand(companyId, entityId, file.FileName, buffer.ToArray()), ct)
                    .ConfigureAwait(false)).ToHttp();
            })
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(Gebeta.Core.Domain.Attachments.Attachment.MaxBytes + 64 * 1024))
            .WithName("AddAttachment").RequirePermission(CorePermissions.AttachmentsManage);

        // Always a download, never shown in the page: an uploaded file cannot run as part of Gebeta.
        files.MapGet("/{id:guid}/content", async (Guid id, HttpResponse response,
                IQueryHandler<Gebeta.Core.Application.Attachments.GetAttachmentFileQuery, Gebeta.Core.Application.Attachments.AttachmentFile> h, CancellationToken ct) =>
            {
                var file = await h.Handle(new Gebeta.Core.Application.Attachments.GetAttachmentFileQuery(id), ct).ConfigureAwait(false);
                if (file.IsFailure)
                {
                    return file.ToHttp();
                }

                response.Headers["X-Content-Type-Options"] = "nosniff";
                return Results.File(file.Value.Bytes, file.Value.ContentType, file.Value.FileName);
            })
            .WithName("GetAttachmentContent").RequirePermission(CorePermissions.AttachmentsRead);
        files.MapDelete("/{id:guid}", async (Guid id, ICommandHandler<Gebeta.Core.Application.Attachments.RemoveAttachmentCommand, AttachmentDto> h, CancellationToken ct) =>
                (await h.Handle(new Gebeta.Core.Application.Attachments.RemoveAttachmentCommand(id), ct).ConfigureAwait(false)).ToHttp())
            .WithName("RemoveAttachment").RequirePermission(CorePermissions.AttachmentsManage);
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

        employees.MapPut("/{id:guid}", async (Guid id, EmployeeBody b, ICommandHandler<UpdateEmployeeCommand, EmployeeDto> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdateEmployeeCommand(id, b.FirstName, b.FatherName, b.GrandfatherName, b.Gender, b.DateOfJoining, b.Tin), ct).ConfigureAwait(false)).ToHttp())
            .WithName("UpdateEmployee")
            .RequirePermission(CorePermissions.EmployeesManage);

        employees.MapPost("/{id:guid}/leaving", async (Guid id, LeavingBody b, ICommandHandler<SetEmployeeLeavingCommand, EmployeeDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SetEmployeeLeavingCommand(id, b.DateOfLeaving), ct).ConfigureAwait(false)).ToHttp())
            .WithName("SetEmployeeLeaving")
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
        // Any signed-in user: an amount written out for vouchers and cheques, in both languages (docs/ethiopia.md §5).
        endpoints.MapGet("/api/core/amount-in-words", (decimal amount, string? currency, bool? withOnly) =>
            {
                var code = string.IsNullOrWhiteSpace(currency) ? "ETB" : currency.Trim();
                return Math.Abs(amount) > Gebeta.SharedKernel.Ethiopia.AmountInWords.Maximum
                    ? Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: $"Amounts in words go up to {Gebeta.SharedKernel.Ethiopia.AmountInWords.Maximum:N2}.")
                    : Results.Ok(new AmountInWordsDto(Gebeta.SharedKernel.Ethiopia.AmountInWords.English(amount, code),
                        Gebeta.SharedKernel.Ethiopia.AmountInWords.Amharic(amount, code, withOnly ?? false)));
            })
            .WithName("AmountInWords").WithTags("Core");

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

        companies.MapPut("/{id:guid}", async (Guid id, CompanyBody b, ICommandHandler<UpdateCompanyCommand, CompanyDto> handler, CancellationToken ct) =>
                (await handler.Handle(new UpdateCompanyCommand(id, b.Name, b.VatRegistrationNumber, b.TradeLicenseNumber), ct).ConfigureAwait(false)).ToHttp())
            .WithName("UpdateCompany")
            .RequirePermission(CorePermissions.CompaniesManage);
    }

    private static void MapUsersAndRoles(IEndpointRouteBuilder endpoints)
    {
        var users = endpoints.MapGroup("/api/core/users").WithTags("Core: Users");

        users.MapGet("/", async (IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>> handler, CancellationToken ct) =>
                (await handler.Handle(new ListUsersQuery(), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ListUsers")
            .RequirePermission(CorePermissions.UsersRead);

        users.MapPost("/", async (InviteUserCommand command, ICommandHandler<InviteUserCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(command, ct).ConfigureAwait(false)).ToHttp())
            .WithName("InviteUser")
            .RequirePermission(CorePermissions.UsersManage);

        users.MapPut("/{userId:guid}/employee", async (Guid userId, EmployeeLinkBody body, ICommandHandler<LinkUserEmployeeCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(new LinkUserEmployeeCommand(userId, body.EmployeeId), ct).ConfigureAwait(false)).ToHttp())
            .WithName("LinkUserEmployee")
            .RequirePermission(CorePermissions.UsersManage);

        users.MapPost("/{userId:guid}/deactivate", async (Guid userId, ICommandHandler<SetUserActiveCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SetUserActiveCommand(userId, false), ct).ConfigureAwait(false)).ToHttp())
            .WithName("DeactivateUser")
            .RequirePermission(CorePermissions.UsersManage);

        users.MapPost("/{userId:guid}/activate", async (Guid userId, ICommandHandler<SetUserActiveCommand, UserDto> handler, CancellationToken ct) =>
                (await handler.Handle(new SetUserActiveCommand(userId, true), ct).ConfigureAwait(false)).ToHttp())
            .WithName("ActivateUser")
            .RequirePermission(CorePermissions.UsersManage);

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

public sealed record PartyBody(string Name, Gebeta.Core.Domain.Parties.PartyType Type, string? Tin, string? VatRegistrationNumber, string? Phone);

public sealed record EmployeeBody(string FirstName, string FatherName, string? GrandfatherName, Gebeta.Core.Domain.Employees.Gender Gender, DateOnly DateOfJoining, string? Tin);

public sealed record AlertRuleBody(string? Name, int? Threshold, IReadOnlyList<string>? UserNames, IReadOnlyList<string>? RoleNames);

public sealed record LeavingBody(DateOnly? DateOfLeaving);

public sealed record CompanyBody(string Name, string? VatRegistrationNumber, string? TradeLicenseNumber);

public sealed record ItemBody(string Name, string Uom, string? Group, bool IsStockItem, bool IsActive = true);

public sealed record EmployeeLinkBody(Guid? EmployeeId);

public sealed record AmountInWordsDto(string English, string Amharic);

public sealed record CostCenterBody(string Name, bool IsActive);
