using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Alerts;

/// <summary>
/// A saved alert (docs/requirements/sap-features.md #9): a condition a module offers, for one company, with a threshold, the people to tell (named
/// users and/or every holder of named roles) and an on/off switch. It notifies and never blocks anything. What it has already told people is kept
/// as <see cref="AlertNotified"/> rows so the same item is not announced on every run.
/// </summary>
public sealed class AlertRule : Entity
{
    public const int MaxName = 120;
    public const int MaxRecipients = 50;
    public const int MaxConditionId = 100;

    private List<string> _userNames = [];
    private List<string> _roleNames = [];

    private AlertRule()
    {
        ConditionId = string.Empty;
        Name = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string ConditionId { get; private set; }
    public string Name { get; private set; }

    /// <summary>Days, percent or instalments depending on the condition; null for a condition without one.</summary>
    public int? Threshold { get; private set; }

    /// <summary>Usernames, lower case.</summary>
    public IReadOnlyList<string> UserNames => _userNames;

    public IReadOnlyList<string> RoleNames => _roleNames;
    public bool IsEnabled { get; private set; }

    /// <summary>The date (in Ethiopia) of the last scheduled or manual run; the daily schedule runs a rule once for each date.</summary>
    public DateOnly? LastRunOn { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }
    public int LastMatchCount { get; private set; }
    public int LastNotifiedCount { get; private set; }
    public string? LastError { get; private set; }

    public static Result<AlertRule> Create(Guid companyId, string conditionId, string? name, int? threshold, IEnumerable<string>? userNames, IEnumerable<string>? roleNames,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(conditionId) || conditionId.Trim().Length > MaxConditionId)
        {
            return Error.Validation("Choose the condition to alert on.");
        }

        var rule = new AlertRule { CompanyId = companyId, ConditionId = conditionId.Trim(), IsEnabled = true, CreatedAt = now };
        var set = rule.Set(name, threshold, userNames, roleNames);
        return set.IsFailure ? set.Error! : rule;
    }

    public Result Update(string? name, int? threshold, IEnumerable<string>? userNames, IEnumerable<string>? roleNames) => Set(name, threshold, userNames, roleNames);

    public void SetEnabled(bool enabled) => IsEnabled = enabled;

    /// <summary>Records a run. <paramref name="error"/> null means the condition was evaluated; the counts then say what it found.</summary>
    public void RecordRun(DateOnly on, DateTimeOffset at, int matched, int notified, string? error)
    {
        LastRunOn = on;
        LastRunAt = at;
        LastError = error is null ? null : error.Length <= 500 ? error : error[..500];
        if (error is null)
        {
            LastMatchCount = matched;
            LastNotifiedCount = notified;
        }
    }

    private Result Set(string? name, int? threshold, IEnumerable<string>? userNames, IEnumerable<string>? roleNames)
    {
        var users = (userNames ?? []).Select(u => u?.Trim().ToLowerInvariant() ?? string.Empty).Where(u => u.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        var roles = (roleNames ?? []).Select(r => r?.Trim() ?? string.Empty).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (users.Count == 0 && roles.Count == 0)
        {
            return Result.Failure(Error.Validation("Name at least one user or role to tell."));
        }

        if (users.Count > MaxRecipients || roles.Count > MaxRecipients)
        {
            return Result.Failure(Error.Validation($"At most {MaxRecipients} users and {MaxRecipients} roles."));
        }

        var label = string.IsNullOrWhiteSpace(name) ? ConditionId : name.Trim();
        if (label.Length > MaxName)
        {
            return Result.Failure(Error.Validation($"The name is at most {MaxName} characters."));
        }

        Name = label;
        Threshold = threshold;
        _userNames = users;
        _roleNames = roles;
        return Result.Success();
    }
}

/// <summary>
/// An item a rule has already told its recipients about. It stays while the item keeps matching; when the item no longer matches the row is
/// removed, so an item that comes back is announced again.
/// </summary>
public sealed class AlertNotified
{
    public const int MaxKey = 200;

    private AlertNotified()
    {
        ItemKey = string.Empty;
    }

    public AlertNotified(Guid ruleId, string itemKey, DateTimeOffset notifiedAt)
    {
        RuleId = ruleId;
        ItemKey = itemKey.Length <= MaxKey ? itemKey : itemKey[..MaxKey];
        NotifiedAt = notifiedAt;
    }

    public Guid RuleId { get; private set; }
    public string ItemKey { get; private set; }
    public DateTimeOffset NotifiedAt { get; private set; }
}

public interface IAlertRuleRepository
{
    void Add(AlertRule rule);
    Task<AlertRule?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The rule, held by a row lock until the transaction ends so two runs (two servers) never run it together; null when another run holds it. Needs a transaction.</summary>
    Task<AlertRule?> TryLockAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<AlertRule>> ListAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>Ids of enabled rules that have not yet run on this date.</summary>
    Task<IReadOnlyList<Guid>> ListDueAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Removes the rule; what it remembers goes with it.</summary>
    void Remove(AlertRule rule);

    Task<IReadOnlyList<AlertNotified>> ListNotifiedAsync(Guid ruleId, CancellationToken cancellationToken);
    void AddNotified(IEnumerable<AlertNotified> rows);
    void RemoveNotified(IEnumerable<AlertNotified> rows);
}
