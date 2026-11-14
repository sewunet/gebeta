using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
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
        return new MeDto(l.User.Id, l.User.Username, l.User.Email, l.User.FullName, l.User.Language, l.User.Calendar.ToString(),
            l.User.DefaultCompanyId, l.Roles.Select(r => r.Name).Order(StringComparer.Ordinal).ToList(), permissions);
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
            u.RoleIds.Where(allRoles.ContainsKey).Select(id => allRoles[id]).Order(StringComparer.Ordinal).ToList())).ToList());
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
        return await SaveAsync(user, cancellationToken).ConfigureAwait(false);
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
        return await SaveAsync(user, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<UserDto>> SaveAsync(User user, CancellationToken cancellationToken)
    {
        user.ModifiedAt = clock.GetUtcNow();
        user.ModifiedBy = (await context.GetAsync(cancellationToken).ConfigureAwait(false))?.User.Username;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var names = (await roles.GetManyAsync(user.RoleIds.ToList(), cancellationToken).ConfigureAwait(false)).Select(r => r.Name).Order(StringComparer.Ordinal).ToList();
        return new UserDto(user.Id, user.Username, user.Email, user.FullName, user.IsActive, names);
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
