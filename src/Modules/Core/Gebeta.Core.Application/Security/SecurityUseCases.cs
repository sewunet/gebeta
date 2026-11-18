using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Security;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Security;

// ---- The signed-in user ----

public sealed record GetMeQuery : IQuery<MeDto>;

public sealed class GetMeHandler(UserContext context) : IQueryHandler<GetMeQuery, MeDto>
{
    public async Task<Result<MeDto>> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        var loaded = await context.GetAsync(cancellationToken).ConfigureAwait(false);
        if (loaded is not { } l)
        {
            return Error.NotFound("User", "current");
        }

        var permissions = l.Roles.SelectMany(r => r.Permissions).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var accessible = await context.GetAccessibleCompanyIdsAsync(cancellationToken).ConfigureAwait(false);
        return new MeDto(l.User.Id, l.User.Username, l.User.Email, l.User.FullName, l.User.Language, l.User.Calendar.ToString(),
            l.User.DefaultCompanyId, l.Roles.Select(r => r.Name).Order(StringComparer.Ordinal).ToList(), permissions,
            AccessAllCompanies: accessible is null, CompanyIds: l.User.CompanyIds.ToList());
    }
}

public sealed record UpdateMyPreferencesCommand(string Language, CalendarPreference Calendar, Guid? DefaultCompanyId) : ICommand<MeDto>;

public sealed class UpdateMyPreferencesHandler(UserContext context, ICoreUnitOfWork unitOfWork, GetMeHandler me, TimeProvider clock)
    : ICommandHandler<UpdateMyPreferencesCommand, MeDto>
{
    public async Task<Result<MeDto>> Handle(UpdateMyPreferencesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await context.GetAsync(cancellationToken).ConfigureAwait(false) is not { } l)
        {
            return Error.NotFound("User", "current");
        }

        if (command.DefaultCompanyId is { } companyId && !await context.CanAccessAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Validation("The default company must be one you have access to.");
        }

        var result = l.User.SetPreferences(command.Language, command.Calendar, command.DefaultCompanyId);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        l.User.ModifiedAt = clock.GetUtcNow();
        l.User.ModifiedBy = l.User.Username;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await me.Handle(new GetMeQuery(), cancellationToken).ConfigureAwait(false);
    }
}

// ---- Users ----

public sealed record ListUsersQuery : IQuery<IReadOnlyList<UserDto>>;

public sealed class ListUsersHandler(IUserRepository users, IRoleRepository roles) : IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>>
{
    public async Task<Result<IReadOnlyList<UserDto>>> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var allRoles = (await roles.ListAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(r => r.Id, r => r.Name);
        var all = await users.ListAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<UserDto>>(all.Select(u => new UserDto(u.Id, u.Username, u.Email, u.FullName, u.IsActive,
            u.RoleIds.Where(allRoles.ContainsKey).Select(id => allRoles[id]).Order(StringComparer.Ordinal).ToList(), u.CompanyIds.ToList(), u.IsInvited)).ToList());
    }
}

public sealed record AssignRoleCommand(Guid UserId, Guid RoleId) : ICommand<UserDto>;

public sealed record RemoveRoleCommand(Guid UserId, Guid RoleId) : ICommand<UserDto>;

public sealed class UserRoleHandler(IUserRepository users, IRoleRepository roles, ICoreUnitOfWork unitOfWork, UserContext context, TimeProvider clock)
    : ICommandHandler<AssignRoleCommand, UserDto>, ICommandHandler<RemoveRoleCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await users.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("User", command.UserId);
        }

        var role = await roles.GetAsync(command.RoleId, cancellationToken).ConfigureAwait(false);
        if (role is null)
        {
            return Error.NotFound("Role", command.RoleId);
        }

        user.AssignRole(role);
        return await SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<UserDto>> Handle(RemoveRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await users.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("User", command.UserId);
        }

        var role = await roles.GetAsync(command.RoleId, cancellationToken).ConfigureAwait(false);
        if (role?.Name == Role.SystemManager && (await context.GetAsync(cancellationToken).ConfigureAwait(false))?.User.Id == user.Id)
        {
            return Error.Conflict("You cannot remove your own System Manager role.");
        }

        user.RemoveRole(command.RoleId);
        return await SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Result<UserDto>> SaveUserAsync(User user, CancellationToken cancellationToken)
    {
        user.ModifiedAt = clock.GetUtcNow();
        user.ModifiedBy = (await context.GetAsync(cancellationToken).ConfigureAwait(false))?.User.Username;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var names = (await roles.GetManyAsync(user.RoleIds.ToList(), cancellationToken).ConfigureAwait(false)).Select(r => r.Name).Order(StringComparer.Ordinal).ToList();
        return new UserDto(user.Id, user.Username, user.Email, user.FullName, user.IsActive, names, user.CompanyIds.ToList(), user.IsInvited);
    }
}

/// <summary>
/// Sets up a user before their first sign-in, with roles (by name) and the companies they may work in. Their Keycloak account is made
/// separately (by an administrator); the first sign-in with this username and email links it.
/// </summary>
public sealed record InviteUserCommand(string Username, string Email, string? FullName, IReadOnlyList<string>? Roles, IReadOnlyList<Guid>? CompanyIds) : ICommand<UserDto>;

public sealed class InviteUserHandler(IUserRepository users, IRoleRepository roles, ICompanyRepository companies, UserRoleHandler saver)
    : ICommandHandler<InviteUserCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(InviteUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var created = User.Invite(command.Username ?? string.Empty, command.Email, command.FullName);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var user = created.Value;
        if (await users.UsernameExistsAsync(user.Username, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"There is already a user {user.Username}.");
        }

        foreach (var name in command.Roles ?? [])
        {
            if (await roles.GetByNameAsync(name, cancellationToken).ConfigureAwait(false) is not { } role)
            {
                return Error.Validation($"There is no role named {name}.");
            }

            user.AssignRole(role);
        }

        foreach (var companyId in command.CompanyIds ?? [])
        {
            if (await companies.GetAsync(companyId, cancellationToken).ConfigureAwait(false) is null)
            {
                return Error.NotFound("Company", companyId);
            }

            user.GrantCompany(companyId);
        }

        users.Add(user);
        return await saver.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record GrantCompanyAccessCommand(Guid UserId, Guid CompanyId) : ICommand<UserDto>;

public sealed record RevokeCompanyAccessCommand(Guid UserId, Guid CompanyId) : ICommand<UserDto>;

public sealed class CompanyAccessHandler(IUserRepository users, ICompanyRepository companies, UserRoleHandler roleHandler)
    : ICommandHandler<GrantCompanyAccessCommand, UserDto>, ICommandHandler<RevokeCompanyAccessCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(GrantCompanyAccessCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await users.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("User", command.UserId);
        }

        if (await companies.GetAsync(command.CompanyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        user.GrantCompany(command.CompanyId);
        return await roleHandler.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<UserDto>> Handle(RevokeCompanyAccessCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await users.GetAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return Error.NotFound("User", command.UserId);
        }

        user.RevokeCompany(command.CompanyId);
        return await roleHandler.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
    }
}

// ---- Roles ----

public sealed record ListRolesQuery : IQuery<IReadOnlyList<RoleDto>>;

public sealed class ListRolesHandler(IRoleRepository roles) : IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>>
{
    public async Task<Result<IReadOnlyList<RoleDto>>> Handle(ListRolesQuery query, CancellationToken cancellationToken) =>
        Result.Success<IReadOnlyList<RoleDto>>((await roles.ListAsync(cancellationToken).ConfigureAwait(false)).Select(RoleMapping.ToDto).ToList());
}

public sealed record CreateRoleCommand(string Name, string? Description, IReadOnlyList<string> Permissions) : ICommand<RoleDto>;

public sealed class CreateRoleHandler(IRoleRepository roles, ICoreUnitOfWork unitOfWork, TimeProvider clock) : ICommandHandler<CreateRoleCommand, RoleDto>
{
    public async Task<Result<RoleDto>> Handle(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var created = Role.Create(command.Name, command.Description);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var role = created.Value;
        foreach (var permission in command.Permissions ?? [])
        {
            var granted = role.Grant(permission);
            if (granted.IsFailure)
            {
                return granted.Error!;
            }
        }

        if (await roles.GetByNameAsync(role.Name, cancellationToken).ConfigureAwait(false) is not null)
        {
            return Error.Conflict($"A role named '{role.Name}' already exists.");
        }

        role.CreatedAt = clock.GetUtcNow();
        roles.Add(role);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RoleMapping.ToDto(role);
    }
}

internal static class RoleMapping
{
    public static RoleDto ToDto(Role r) => new(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions.ToList());
}
