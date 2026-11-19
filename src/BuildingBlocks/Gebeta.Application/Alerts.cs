namespace Gebeta.Application;

/// <summary>
/// The range a condition's threshold may take and what it counts (<c>days</c>, <c>percent</c>, <c>instalments</c>). A condition without a threshold has none.
/// </summary>
public sealed record AlertThreshold(string Unit, int Default, int Min, int Max);

/// <summary>What a condition is asked: the company, the threshold of the rule (the condition's default when it has none) and the date to judge by.</summary>
public sealed record AlertContext(Guid CompanyId, int Threshold, DateOnly Today);

/// <summary>
/// One thing that matches a condition. <paramref name="Key"/> identifies it across runs (a record id, or a record id with a part of it): the same key
/// means the same item, so a person is told about it once and told again only after it has cleared and come back. The link is the record's kind
/// (the names the document flow uses) and id, so the inbox can open it.
/// </summary>
public sealed record AlertItem(string Key, string Title, string? Body = null, string? LinkKind = null, Guid? LinkId = null,
    NotificationSeverity Severity = NotificationSeverity.Warning);

/// <summary>
/// A named check a module offers for alerts (docs/requirements/sap-features.md #9): "invoices overdue", "a loan nearly repaid". Core stores rules that
/// pick a condition, a company, recipients and a threshold, and runs them on a schedule; the module only answers what matches today. A condition is
/// fixed, tested code (never a query typed in by a user), reads only its own module's data and must never change anything. Modules register their
/// conditions as plain scoped services; Core works with none installed.
/// </summary>
public interface IAlertCondition
{
    /// <summary>Stable, lower case, starts with the module: <c>accounting.overdue-sales-invoices</c>.</summary>
    string Id { get; }

    /// <summary>A short name in English.</summary>
    string Name { get; }

    string Description { get; }

    /// <summary>The permission that reads what this condition reveals. Only a holder can set up a rule on it and only holders are told.</summary>
    string RequiredPermission { get; }

    AlertThreshold? Threshold { get; }

    /// <summary>The items matching today, in a stable order, at most <see cref="AlertLimits.MaxItems"/> (Core cuts the rest).</summary>
    Task<IReadOnlyList<AlertItem>> EvaluateAsync(AlertContext context, CancellationToken cancellationToken);
}

public static class AlertLimits
{
    /// <summary>Most items one run of one rule looks at.</summary>
    public const int MaxItems = 500;

    /// <summary>
    /// When more than this many items are new in one run, the recipients get one summary message instead of one each, so a rule switched on over
    /// a backlog does not flood the inbox.
    /// </summary>
    public const int MaxIndividualMessages = 10;
}
