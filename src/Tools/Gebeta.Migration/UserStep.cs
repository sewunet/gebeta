using System.Text.Json;
using Gebeta.Core.Application.Security;
using Gebeta.Core.Contracts;

namespace Gebeta.Migration;

/// <summary>
/// Users and their roles (docs/data-migration.md step 2). Gebeta keeps the profile, roles and companies; the sign-in identity lives in
/// Keycloak. Users are invited in Gebeta (set up ahead, linked on their first sign-in with the same username and email), and a
/// Keycloak partial-import file is written for an administrator to review and import: no passwords, a new one is set at first login.
/// </summary>
public sealed partial class MigrationRun
{
    /// <summary>ERPNext's standard roles and the Gebeta system roles that do the same work.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RoleMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["System Manager"] = ["System Manager"],
        ["Accounts Manager"] = ["Accountant"],
        ["Accounts User"] = ["Accountant"],
        ["Auditor"] = ["Accountant"],
        ["Stock Manager"] = ["Store Keeper"],
        ["Stock User"] = ["Store Keeper"],
        ["Item Manager"] = ["Store Keeper"],
        ["Sales Manager"] = ["Sales Officer"],
        ["Sales User"] = ["Sales Officer"],
        ["Sales Master Manager"] = ["Sales Officer"],
        ["Purchase Manager"] = ["Purchaser"],
        ["Purchase User"] = ["Purchaser"],
        ["Purchase Master Manager"] = ["Purchaser"],
        ["HR Manager"] = ["HR Officer", "Payroll Officer"], // payroll is HR's in ERPNext
        ["HR User"] = ["HR Officer"],
        ["Leave Approver"] = ["HR Officer"],
        ["Cashier"] = ["Cashier"],
    };

    /// <summary>
    /// Users come over with no roles unless an administrator gives them roles in the reviewed roles file. ERPNext sites often give
    /// most users every role, System Manager included; copying that would make them all administrators of Gebeta.
    /// </summary>
    private async Task<StepReport> UsersAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("users");
        var users = (await source.UsersAsync(cancellationToken).ConfigureAwait(false))
            .Where(u => u.Companies.Count == 0 || u.Companies.Contains(options.Company, StringComparer.Ordinal)).ToList();

        var unmapped = users.SelectMany(u => u.Roles).Where(r => !RoleMap.ContainsKey(r)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (unmapped.Count > 0)
        {
            report.Warnings.Add($"{unmapped.Count} ERPNext roles have no Gebeta counterpart ({string.Join(", ", unmapped.Take(8))}{(unmapped.Count > 8 ? ", ..." : "")}).");
        }

        var admins = users.Count(u => u.Roles.Contains("System Manager", StringComparer.Ordinal));
        if (admins > 0)
        {
            report.Warnings.Add($"{admins} users are System Managers in ERPNext. The migration makes no one a Gebeta System Manager on its own: name the administrators in the roles file.");
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>>? chosen = null;
        if (options.UserRolesPath is { } rolesPath)
        {
            if (File.Exists(rolesPath))
            {
                chosen = ReadUserRoles(rolesPath);
            }
            else
            {
                await File.WriteAllTextAsync(rolesPath, UserRolesFile(users), cancellationToken).ConfigureAwait(false);
                report.Warnings.Add($"Wrote {rolesPath}: fill gebeta_roles for each user (suggested_roles is a hint), then run again. Until then users come over with no roles.");
            }
        }
        else
        {
            report.Warnings.Add("No --user-roles file: users come over with no roles.");
        }

        var known = await KnownAsync("User", cancellationToken).ConfigureAwait(false);
        var existing = options.DryRun ? new Dictionary<string, UserDto>()
            : (await Query<ListUsersQuery, IReadOnlyList<UserDto>>(new ListUsersQuery(), cancellationToken).ConfigureAwait(false)).Value.ToDictionary(u => u.Username, StringComparer.OrdinalIgnoreCase);
        var byId = existing.Values.ToDictionary(u => u.Id);
        var roleIds = options.DryRun ? new Dictionary<string, Guid>()
            : (await Query<ListRolesQuery, IReadOnlyList<RoleDto>>(new ListRolesQuery(), cancellationToken).ConfigureAwait(false)).Value.ToDictionary(r => r.Name, r => r.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var u in users)
        {
            var roles = chosen?.GetValueOrDefault(u.Name) ?? [];
            var unknownRoles = options.DryRun ? [] : roles.Where(r => !roleIds.ContainsKey(r)).ToList();
            if (unknownRoles.Count > 0)
            {
                report.Failures.Add($"{u.Name}: no Gebeta role named {string.Join(", ", unknownRoles)} (see the roles in Gebeta).");
                continue;
            }

            if (known.TryGetValue(u.Name, out var id))
            {
                report.AlreadyMigrated++;
                // Roles chosen after the first run reach users who have not signed in yet; nothing is ever taken away.
                if (byId.TryGetValue(id, out var current) && current.IsInvited)
                {
                    foreach (var role in roles.Where(r => !current.Roles.Contains(r, StringComparer.OrdinalIgnoreCase)))
                    {
                        var assigned = await Handle<AssignRoleCommand, UserDto>(new AssignRoleCommand(id, roleIds[role]), cancellationToken).ConfigureAwait(false);
                        if (assigned.IsFailure)
                        {
                            report.Failures.Add($"{u.Name}: {role}: {assigned.Error!.Message}");
                        }
                    }
                }

                continue;
            }

            var username = (u.Username ?? u.Email).Trim().ToLowerInvariant();
            if (existing.TryGetValue(username, out var signedIn))
            {
                // Someone who already uses Gebeta: give them the company, but their roles stay as an administrator set them.
                if (!signedIn.CompanyIds.Contains(_companyId))
                {
                    var granted = await Handle<GrantCompanyAccessCommand, UserDto>(new GrantCompanyAccessCommand(signedIn.Id, _companyId), cancellationToken).ConfigureAwait(false);
                    if (granted.IsFailure)
                    {
                        report.Failures.Add($"{u.Name}: {granted.Error!.Message}");
                        continue;
                    }
                }

                await idMap.AddAsync(options.Company, "User", u.Name, signedIn.Id, cancellationToken).ConfigureAwait(false);
                report.Warnings.Add($"{u.Name}: Gebeta already has user {username}; it gets this company, its roles are left as they are.");
                report.Adopted++;
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var invited = await Handle<InviteUserCommand, UserDto>(new InviteUserCommand(username, u.Email, u.FullName, roles, [_companyId]), cancellationToken).ConfigureAwait(false);
            if (invited.IsFailure)
            {
                report.Failures.Add($"{u.Name}: {invited.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "User", u.Name, invited.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        if (options.KeycloakUsersPath is { } path && !options.DryRun)
        {
            var mapped = (await idMap.LoadAsync(options.Company, "User", cancellationToken).ConfigureAwait(false)).Values.ToHashSet();
            var pending = (await Query<ListUsersQuery, IReadOnlyList<UserDto>>(new ListUsersQuery(), cancellationToken).ConfigureAwait(false)).Value
                .Where(x => x.IsInvited && mapped.Contains(x.Id)).ToList();
            await File.WriteAllTextAsync(path, KeycloakImport(pending), cancellationToken).ConfigureAwait(false);
            report.Warnings.Add($"Wrote {path}: {pending.Count} users who have not signed in yet, for Keycloak's partial import (no passwords; each sets one at first login).");
        }

        await CheckAsync(report, "User", users.Count, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>The review file: per user the relevant ERPNext roles, suggested Gebeta roles (never System Manager), and an empty gebeta_roles to fill.</summary>
    public static string UserRolesFile(IEnumerable<ErpUser> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        var text = new System.Text.StringBuilder("erpnext_user,username,email,full_name,erpnext_roles,suggested_roles,gebeta_roles\n");
        foreach (var u in users)
        {
            var relevant = u.Roles.Where(RoleMap.ContainsKey).ToList();
            var suggested = relevant.Where(r => r != "System Manager").SelectMany(r => RoleMap[r]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            text.AppendLine(string.Join(',', new[] { u.Name, (u.Username ?? u.Email).ToLowerInvariant(), u.Email, u.FullName ?? "", string.Join("; ", relevant),
                string.Join("; ", suggested), "" }.Select(Csv.Quote)));
        }

        return text.ToString();
    }

    /// <summary>erpnext_user → the Gebeta roles an administrator chose (the gebeta_roles column; ";"-separated).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadUserRoles(string path)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var fields in File.ReadLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).Select(Csv.Split))
        {
            result[fields[0]] = (fields.Count > 6 ? fields[6] : string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return result;
    }

    private static readonly string[] SetPasswordAtFirstLogin = ["UPDATE_PASSWORD"];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Keycloak partial-import JSON: the accounts only, enabled, with a new password to choose at first login.</summary>
    public static string KeycloakImport(IEnumerable<UserDto> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        var list = users.Select(u =>
        {
            var words = (u.FullName ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return new
            {
                username = u.Username,
                email = u.Email,
                firstName = words.ElementAtOrDefault(0),
                lastName = words.ElementAtOrDefault(1),
                enabled = true,
                emailVerified = false,
                requiredActions = SetPasswordAtFirstLogin,
            };
        }).ToList();
        return JsonSerializer.Serialize(new { users = list }, Indented);
    }
}
