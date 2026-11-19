namespace Gebeta.Core.Contracts;

/// <summary>A condition an installed module offers. ThresholdUnit is null when the condition has no threshold.</summary>
public sealed record AlertConditionDto(string Id, string Name, string Description, string RequiredPermission, string? ThresholdUnit, int? DefaultThreshold, int? MinThreshold, int? MaxThreshold);

/// <summary>
/// An alert rule. LastRunOn is the date it last ran (null: never); LastMatchCount how many items matched then and LastNotifiedCount how many of them
/// were new. LastError is set when the condition failed or is no longer installed (nothing was sent or cleared then). ConditionAvailable is false when
/// the module offering the condition is not installed.
/// </summary>
public sealed record AlertRuleDto(Guid Id, Guid CompanyId, string ConditionId, string Name, int? Threshold, IReadOnlyList<string> UserNames, IReadOnlyList<string> RoleNames, bool IsEnabled,
    DateOnly? LastRunOn, DateTimeOffset? LastRunAt, int LastMatchCount, int LastNotifiedCount, string? LastError, bool ConditionAvailable);

/// <summary>What a run found: Matched items now, Notified of them new (not told before), Cleared that were told before and no longer match. Skipped is true when another run held the rule.</summary>
public sealed record AlertRunDto(Guid RuleId, int Matched, int Notified, int Cleared, bool Skipped, string? Error);
