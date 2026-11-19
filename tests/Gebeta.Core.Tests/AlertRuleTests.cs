using Gebeta.Core.Domain.Alerts;

namespace Gebeta.Core.Tests;

public class AlertRuleTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void A_rule_needs_a_condition_and_somebody_to_tell_and_is_created_on()
    {
        Assert.True(AlertRule.Create(Company, " ", null, null, ["ann"], [], At).IsFailure);
        Assert.True(AlertRule.Create(Company, "buying.agreements-expiring", null, 30, [], [], At).IsFailure);
        Assert.True(AlertRule.Create(Company, "buying.agreements-expiring", null, 30, ["  "], [" "], At).IsFailure); // blanks are nobody

        var rule = AlertRule.Create(Company, " buying.agreements-expiring ", null, 30, ["Ann", "ann ", "BOB"], ["Purchaser", "purchaser"], At).Value;
        Assert.Equal(("buying.agreements-expiring", "buying.agreements-expiring", 30, true, At), (rule.ConditionId, rule.Name, rule.Threshold, rule.IsEnabled, rule.CreatedAt));
        Assert.Equal(["ann", "bob"], rule.UserNames);   // usernames in lower case, once each
        Assert.Equal(["Purchaser"], rule.RoleNames);    // role names once each, without regard to case
        Assert.Null(rule.LastRunOn);
    }

    [Fact]
    public void Names_and_recipient_lists_are_limited()
    {
        Assert.True(AlertRule.Create(Company, "c.x", new string('n', AlertRule.MaxName + 1), null, ["ann"], [], At).IsFailure);
        var many = Enumerable.Range(1, AlertRule.MaxRecipients + 1).Select(i => $"user{i}").ToList();
        Assert.True(AlertRule.Create(Company, "c.x", null, null, many, [], At).IsFailure);
        Assert.True(AlertRule.Create(Company, "c.x", null, null, many.Take(AlertRule.MaxRecipients), [], At).IsSuccess);
    }

    [Fact]
    public void A_failed_update_changes_nothing_and_a_good_one_replaces_the_recipients()
    {
        var rule = AlertRule.Create(Company, "c.x", "Mine", 5, ["ann"], [], At).Value;
        Assert.True(rule.Update("Other", 9, [], []).IsFailure);
        Assert.Equal(("Mine", 5), (rule.Name, rule.Threshold));
        Assert.Equal(["ann"], rule.UserNames);

        Assert.True(rule.Update("Other", 9, ["bob"], ["Cashier"]).IsSuccess);
        Assert.Equal(("Other", 9), (rule.Name, rule.Threshold));
        Assert.Equal(["bob"], rule.UserNames);
        Assert.Equal(["Cashier"], rule.RoleNames);
    }

    [Fact]
    public void A_run_records_its_counts_and_a_failed_run_keeps_the_last_good_counts_with_the_error()
    {
        var rule = AlertRule.Create(Company, "c.x", null, null, ["ann"], [], At).Value;
        rule.RecordRun(new DateOnly(2026, 11, 10), At, matched: 4, notified: 3, error: null);
        Assert.Equal((new DateOnly(2026, 11, 10), 4, 3, null), (rule.LastRunOn, rule.LastMatchCount, rule.LastNotifiedCount, rule.LastError));

        rule.RecordRun(new DateOnly(2026, 11, 11), At.AddDays(1), 0, 0, new string('e', 900));
        Assert.Equal((new DateOnly(2026, 11, 11), 4, 3, 500), (rule.LastRunOn, rule.LastMatchCount, rule.LastNotifiedCount, rule.LastError!.Length));

        rule.SetEnabled(false);
        Assert.False(rule.IsEnabled);
    }
}
