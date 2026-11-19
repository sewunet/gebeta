using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Alerts;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Security;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Alerts;

public sealed record ListAlertConditionsQuery : IQuery<IReadOnlyList<AlertConditionDto>>;

public sealed record ListAlertRulesQuery(Guid CompanyId) : IQuery<IReadOnlyList<AlertRuleDto>>;

public sealed record CreateAlertRuleCommand(Guid CompanyId, string ConditionId, string? Name, int? Threshold, IReadOnlyList<string>? UserNames, IReadOnlyList<string>? RoleNames) : ICommand<AlertRuleDto>;

public sealed record UpdateAlertRuleCommand(Guid Id, string? Name, int? Threshold, IReadOnlyList<string>? UserNames, IReadOnlyList<string>? RoleNames) : ICommand<AlertRuleDto>;

public sealed record SetAlertRuleEnabledCommand(Guid Id, bool Enabled) : ICommand<AlertRuleDto>;

public sealed record DeleteAlertRuleCommand(Guid Id) : ICommand<AlertRuleDto>;

/// <summary>Runs one rule now. <paramref name="AsOf"/> judges by that date instead of today (to see what would be told on a given day).</summary>
public sealed record RunAlertRuleCommand(Guid Id, DateOnly? AsOf) : ICommand<AlertRunDto>;

/// <summary>
/// Alert rules (docs/requirements/sap-features.md #9). Every rule belongs to a company the caller may work in, and the caller must hold the permission
/// the rule's condition reads (a user who may not see overdue invoices cannot set up, see or run a rule on them): a rule the caller may not touch is
/// answered as not found.
/// </summary>
public sealed class AlertRuleHandler(IAlertRuleRepository rules, IEnumerable<IAlertCondition> conditions, ICompanyRepository companies, ICompanyAccess access, IPermissionChecker permissions,
    IUserRepository users, IRoleRepository roles, AlertRunner runner, Companies.ICoreUnitOfWork unitOfWork, TimeProvider clock)
    : IQueryHandler<ListAlertConditionsQuery, IReadOnlyList<AlertConditionDto>>, IQueryHandler<ListAlertRulesQuery, IReadOnlyList<AlertRuleDto>>,
      ICommandHandler<CreateAlertRuleCommand, AlertRuleDto>, ICommandHandler<UpdateAlertRuleCommand, AlertRuleDto>,
      ICommandHandler<SetAlertRuleEnabledCommand, AlertRuleDto>, ICommandHandler<DeleteAlertRuleCommand, AlertRuleDto>, ICommandHandler<RunAlertRuleCommand, AlertRunDto>
{
    private readonly IReadOnlyList<IAlertCondition> _conditions = conditions.ToList();

    public async Task<Result<IReadOnlyList<AlertConditionDto>>> Handle(ListAlertConditionsQuery query, CancellationToken cancellationToken)
    {
        var visible = new List<AlertConditionDto>();
        foreach (var c in _conditions.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (await permissions.HasPermissionAsync(c.RequiredPermission, cancellationToken).ConfigureAwait(false))
            {
                visible.Add(ToDto(c));
            }
        }

        return Result.Success<IReadOnlyList<AlertConditionDto>>(visible);
    }

    public async Task<Result<IReadOnlyList<AlertRuleDto>>> Handle(ListAlertRulesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var visible = new List<AlertRuleDto>();
        foreach (var rule in await rules.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            if (await MayTouchAsync(rule, cancellationToken).ConfigureAwait(false))
            {
                visible.Add(ToDto(rule));
            }
        }

        return Result.Success<IReadOnlyList<AlertRuleDto>>(visible);
    }

    public async Task<Result<AlertRuleDto>> Handle(CreateAlertRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)
            || await companies.GetAsync(command.CompanyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var condition = _conditions.FirstOrDefault(c => string.Equals(c.Id, command.ConditionId?.Trim(), StringComparison.Ordinal));
        if (condition is null || !await permissions.HasPermissionAsync(condition.RequiredPermission, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Alert condition", command.ConditionId ?? string.Empty);
        }

        if (CheckThreshold(condition, command.Threshold) is { } thresholdError)
        {
            return thresholdError;
        }

        var created = AlertRule.Create(command.CompanyId, condition.Id, command.Name, ThresholdFor(condition, command.Threshold), command.UserNames, command.RoleNames, clock.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error!;
        }

        if (await CheckRecipientsAsync(condition, created.Value, cancellationToken).ConfigureAwait(false) is { } recipientError)
        {
            return recipientError;
        }

        rules.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(created.Value);
    }

    public async Task<Result<AlertRuleDto>> Handle(UpdateAlertRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await OwnAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } rule)
        {
            return Error.NotFound("Alert rule", command.Id);
        }

        var condition = ConditionOf(rule);
        if (condition is null)
        {
            return Error.Conflict("The module offering this condition is not installed, so the rule can only be switched or deleted.");
        }

        if (CheckThreshold(condition, command.Threshold) is { } thresholdError)
        {
            return thresholdError;
        }

        var updated = rule.Update(command.Name, ThresholdFor(condition, command.Threshold), command.UserNames, command.RoleNames);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        if (await CheckRecipientsAsync(condition, rule, cancellationToken).ConfigureAwait(false) is { } recipientError)
        {
            return recipientError;
        }

        rule.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(rule);
    }

    public async Task<Result<AlertRuleDto>> Handle(SetAlertRuleEnabledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await OwnAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } rule)
        {
            return Error.NotFound("Alert rule", command.Id);
        }

        rule.SetEnabled(command.Enabled);
        rule.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(rule);
    }

    public async Task<Result<AlertRuleDto>> Handle(DeleteAlertRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await OwnAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } rule)
        {
            return Error.NotFound("Alert rule", command.Id);
        }

        var dto = ToDto(rule);
        rules.Remove(rule);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return dto;
    }

    public async Task<Result<AlertRunDto>> Handle(RunAlertRuleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await OwnAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } rule)
        {
            return Error.NotFound("Alert rule", command.Id);
        }

        if (!rule.IsEnabled)
        {
            return Error.Validation("Switch the rule on before running it.");
        }

        return await runner.RunAsync(rule.Id, command.AsOf ?? AlertRunner.TodayIn(clock), manual: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AlertRule?> OwnAsync(Guid id, CancellationToken cancellationToken)
    {
        var rule = await rules.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return rule is not null && await access.CanAccessAsync(rule.CompanyId, cancellationToken).ConfigureAwait(false)
            && await MayTouchAsync(rule, cancellationToken).ConfigureAwait(false)
            ? rule
            : null;
    }

    /// <summary>The caller holds what the condition reads. A rule whose condition is not installed has nothing to read, so it can be switched or removed.</summary>
    private async Task<bool> MayTouchAsync(AlertRule rule, CancellationToken cancellationToken) =>
        ConditionOf(rule) is not { } condition || await permissions.HasPermissionAsync(condition.RequiredPermission, cancellationToken).ConfigureAwait(false);

    private IAlertCondition? ConditionOf(AlertRule rule) => _conditions.FirstOrDefault(c => string.Equals(c.Id, rule.ConditionId, StringComparison.Ordinal));

    private static Error? CheckThreshold(IAlertCondition condition, int? threshold) =>
        condition.Threshold is { } t && threshold is { } value && (value < t.Min || value > t.Max)
            ? Error.Validation($"The threshold is between {t.Min} and {t.Max} {t.Unit}.")
            : null;

    private static int? ThresholdFor(IAlertCondition condition, int? threshold) => condition.Threshold is { } t ? threshold ?? t.Default : null;

    /// <summary>Everyone named must exist, and must be someone who may read what the condition reveals (a role must allow the permission; a user must hold it through a role).</summary>
    private async Task<Error?> CheckRecipientsAsync(IAlertCondition condition, AlertRule rule, CancellationToken cancellationToken)
    {
        foreach (var roleName in rule.RoleNames)
        {
            if (await roles.GetByNameAsync(roleName, cancellationToken).ConfigureAwait(false) is not { } role)
            {
                return Error.Validation($"There is no role '{roleName}'.");
            }

            if (!role.Allows(condition.RequiredPermission))
            {
                return Error.Validation($"The role '{role.Name}' may not read what this alert shows ({condition.RequiredPermission}).");
            }
        }

        foreach (var userName in rule.UserNames)
        {
            if (await users.GetByUsernameAsync(userName, cancellationToken).ConfigureAwait(false) is not { } user)
            {
                return Error.Validation($"There is no user '{userName}'.");
            }

            if (!await AlertRunner.MayReadAsync(user, condition.RequiredPermission, roles, cancellationToken).ConfigureAwait(false))
            {
                return Error.Validation($"The user '{user.Username}' may not read what this alert shows ({condition.RequiredPermission}).");
            }
        }

        return null;
    }

    private static AlertConditionDto ToDto(IAlertCondition c) =>
        new(c.Id, c.Name, c.Description, c.RequiredPermission, c.Threshold?.Unit, c.Threshold?.Default, c.Threshold?.Min, c.Threshold?.Max);

    private AlertRuleDto ToDto(AlertRule r) =>
        new(r.Id, r.CompanyId, r.ConditionId, r.Name, r.Threshold, r.UserNames, r.RoleNames, r.IsEnabled, r.LastRunOn, r.LastRunAt, r.LastMatchCount, r.LastNotifiedCount, r.LastError,
            ConditionOf(r) is not null);
}

/// <summary>
/// Runs alert rules, from the daily schedule and from "run now". One run of one rule: lock the rule (so two servers never run it together),
/// ask the condition what matches, tell the recipients about the items they have not been told about, forget the items that no longer match, and
/// record the run, all in one transaction. A condition that fails changes nothing (it must not make every item look cleared and so be announced
/// again). Alerts notify and never block: a run's failure is recorded on the rule and never reaches the business actions.
/// </summary>
public sealed class AlertRunner(IAlertRuleRepository rules, IEnumerable<IAlertCondition> conditions, IUserRepository users, IRoleRepository roles, INotifier notifier,
    Companies.ICoreUnitOfWork unitOfWork, TimeProvider clock)
{
    /// <summary>Ethiopia keeps one time all year (UTC+3), and "today" for an alert is the date there.</summary>
    public static DateOnly TodayIn(TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3)).DateTime);
    }

    public static async Task<bool> MayReadAsync(User user, string permission, IRoleRepository roles, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(roles);
        var held = await roles.GetManyAsync(user.RoleIds.ToList(), cancellationToken).ConfigureAwait(false);
        return held.Any(r => r.Allows(permission));
    }

    /// <summary>
    /// Runs the rule for this date. A scheduled run (<paramref name="manual"/> false) does nothing for a rule that is off or has already run on
    /// this date; a manual run always runs. <c>Skipped</c> in the result means another run held the rule (or it is gone).
    /// </summary>
    public async Task<Result<AlertRunDto>> RunAsync(Guid ruleId, DateOnly today, bool manual, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (await rules.TryLockAsync(ruleId, cancellationToken).ConfigureAwait(false) is not { } rule
            || !rule.IsEnabled || (!manual && rule.LastRunOn is { } last && last >= today))
        {
            return new AlertRunDto(ruleId, 0, 0, 0, Skipped: true, null);
        }

        var now = clock.GetUtcNow();
        var condition = conditions.FirstOrDefault(c => string.Equals(c.Id, rule.ConditionId, StringComparison.Ordinal));
        if (condition is null)
        {
            return await FinishAsync(rule, today, now, 0, 0, 0, "The module offering this condition is not installed.", transaction, cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<AlertItem> found;
        try
        {
            found = await condition.EvaluateAsync(new AlertContext(rule.CompanyId, rule.Threshold ?? condition.Threshold?.Default ?? 0, today), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Application code does not log; the failure is kept on the rule where the screen shows it.
            return await FinishAsync(rule, today, now, 0, 0, 0, $"The condition could not be checked ({ex.GetType().Name}: {ex.Message}).", transaction, cancellationToken).ConfigureAwait(false);
        }

        var matches = found.Where(i => !string.IsNullOrWhiteSpace(i.Key) && !string.IsNullOrWhiteSpace(i.Title))
            .Select(i => i with { Key = Key(i.Key) }).DistinctBy(i => i.Key, StringComparer.Ordinal).Take(AlertLimits.MaxItems).ToList();
        var told = await rules.ListNotifiedAsync(rule.Id, cancellationToken).ConfigureAwait(false);
        var toldKeys = told.Select(t => t.ItemKey).ToHashSet(StringComparer.Ordinal);
        var currentKeys = matches.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var fresh = matches.Where(m => !toldKeys.Contains(m.Key)).ToList();
        var cleared = told.Where(t => !currentKeys.Contains(t.ItemKey)).ToList();

        string? problem = null;
        var notified = 0;
        if (fresh.Count > 0)
        {
            var recipients = await RecipientsAsync(rule, condition.RequiredPermission, cancellationToken).ConfigureAwait(false);
            if (recipients.Count == 0)
            {
                // Nobody to tell: remember nothing, so the items are announced once someone can be told.
                problem = "None of the named users or roles may read what this alert shows.";
                fresh = [];
            }
            else
            {
                await SendAsync(rule, fresh, recipients, cancellationToken).ConfigureAwait(false);
                notified = fresh.Count;
                rules.AddNotified(fresh.Select(f => new AlertNotified(rule.Id, f.Key, now)));
            }
        }

        rules.RemoveNotified(cleared);
        return await FinishAsync(rule, today, now, matches.Count, notified, cleared.Count, problem, transaction, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<AlertRunDto>> FinishAsync(AlertRule rule, DateOnly today, DateTimeOffset now, int matched, int notified, int cleared, string? problem,
        ITransactionScope transaction, CancellationToken cancellationToken)
    {
        rule.RecordRun(today, now, matched, notified, problem);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new AlertRunDto(rule.Id, matched, notified, cleared, Skipped: false, problem);
    }

    private static string Key(string key) => key.Length <= AlertNotified.MaxKey ? key : key[..AlertNotified.MaxKey];

    /// <summary>Named users plus every holder of the named roles, each once, active, and holding the condition's permission through some role.</summary>
    private async Task<IReadOnlyList<string>> RecipientsAsync(AlertRule rule, string permission, CancellationToken cancellationToken)
    {
        var chosen = new Dictionary<Guid, User>();
        foreach (var name in rule.UserNames)
        {
            if (await users.GetByUsernameAsync(name, cancellationToken).ConfigureAwait(false) is { } user)
            {
                chosen[user.Id] = user;
            }
        }

        var roleIds = new HashSet<Guid>();
        foreach (var name in rule.RoleNames)
        {
            if (await roles.GetByNameAsync(name, cancellationToken).ConfigureAwait(false) is { } role)
            {
                roleIds.Add(role.Id);
            }
        }

        if (roleIds.Count > 0)
        {
            foreach (var user in (await users.ListAsync(cancellationToken).ConfigureAwait(false)).Where(u => u.RoleIds.Any(roleIds.Contains)))
            {
                chosen[user.Id] = user;
            }
        }

        var allowed = new List<string>();
        foreach (var user in chosen.Values.OrderBy(u => u.Username, StringComparer.Ordinal))
        {
            if (user.IsActive && await MayReadAsync(user, permission, roles, cancellationToken).ConfigureAwait(false))
            {
                allowed.Add(user.Username);
            }
        }

        return allowed;
    }

    private async Task SendAsync(AlertRule rule, List<AlertItem> fresh, IReadOnlyList<string> recipients, CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationMessage> messages;
        if (fresh.Count <= AlertLimits.MaxIndividualMessages)
        {
            messages = fresh.Select(i => new NotificationMessage(i.Title, i.Body is null ? rule.Name : $"{rule.Name}. {i.Body}", i.LinkKind, i.LinkId, i.Severity)).ToList();
        }
        else
        {
            var listed = string.Join("; ", fresh.Take(5).Select(i => i.Title));
            messages =
            [
                new NotificationMessage($"{rule.Name}: {fresh.Count} new items", $"{listed}; and {fresh.Count - 5} more.", null, null, (NotificationSeverity)fresh.Max(i => (int)i.Severity)),
            ];
        }

        foreach (var message in messages)
        {
            foreach (var username in recipients)
            {
                await notifier.NotifyUserAsync(rule.CompanyId, username, message, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
