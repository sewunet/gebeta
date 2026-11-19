using Gebeta.Application;
using Gebeta.Core.Application.Alerts;
using Gebeta.Core.Domain.Alerts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.ToTable("alert_rule");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ConditionId).HasMaxLength(AlertRule.MaxConditionId).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(AlertRule.MaxName).IsRequired();
        builder.Property(r => r.LastError).HasMaxLength(500);
        builder.Property(r => r.CreatedBy).HasMaxLength(140);
        builder.Property(r => r.ModifiedBy).HasMaxLength(140);
        builder.Property<List<string>>("_userNames").HasColumnName("user_names").IsRequired();
        builder.Property<List<string>>("_roleNames").HasColumnName("role_names").IsRequired();
        builder.Ignore(r => r.UserNames);
        builder.Ignore(r => r.RoleNames);
        builder.Ignore(r => r.DomainEvents);

        builder.HasIndex(r => r.CompanyId, "ix_alert_rule_company").HasDatabaseName("ix_alert_rule_company");

        // The daily schedule looks for enabled rules that have not run today.
        builder.HasIndex(r => r.LastRunOn, "ix_alert_rule_enabled_last_run").HasDatabaseName("ix_alert_rule_enabled_last_run").HasFilter("is_enabled");
    }
}

internal sealed class AlertNotifiedConfiguration : IEntityTypeConfiguration<AlertNotified>
{
    public void Configure(EntityTypeBuilder<AlertNotified> builder)
    {
        builder.ToTable("alert_notified");
        builder.HasKey(n => new { n.RuleId, n.ItemKey });
        builder.Property(n => n.ItemKey).HasMaxLength(AlertNotified.MaxKey);
        builder.HasOne<AlertRule>().WithMany().HasForeignKey(n => n.RuleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AlertRuleRepository(CoreDbContext db) : IAlertRuleRepository
{
    public void Add(AlertRule rule) => db.Set<AlertRule>().Add(rule);

    public Task<AlertRule?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Set<AlertRule>().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<AlertRule?> TryLockAsync(Guid id, CancellationToken cancellationToken)
    {
        // SKIP LOCKED: a second server finding the rule held gets nothing instead of waiting to run it a second time.
        var found = await db.Set<AlertRule>().FromSqlInterpolated($"SELECT * FROM core.alert_rule WHERE id = {id} FOR UPDATE SKIP LOCKED").AsTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (found is not null)
        {
            await db.Entry(found).ReloadAsync(cancellationToken).ConfigureAwait(false); // a copy tracked earlier may be stale
        }

        return found;
    }

    public async Task<IReadOnlyList<AlertRule>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Set<AlertRule>().AsNoTracking().Where(r => r.CompanyId == companyId).OrderBy(r => r.Name).ThenBy(r => r.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Guid>> ListDueAsync(DateOnly today, CancellationToken cancellationToken) =>
        await db.Set<AlertRule>().AsNoTracking().Where(r => r.IsEnabled && (r.LastRunOn == null || r.LastRunOn < today)).OrderBy(r => r.Id).Select(r => r.Id)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Remove(AlertRule rule) => db.Set<AlertRule>().Remove(rule);

    public async Task<IReadOnlyList<AlertNotified>> ListNotifiedAsync(Guid ruleId, CancellationToken cancellationToken) =>
        await db.Set<AlertNotified>().Where(n => n.RuleId == ruleId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void AddNotified(IEnumerable<AlertNotified> rows) => db.Set<AlertNotified>().AddRange(rows);

    public void RemoveNotified(IEnumerable<AlertNotified> rows) => db.Set<AlertNotified>().RemoveRange(rows);
}

/// <summary>Settings of the daily alert run (<c>Alerts</c> section).</summary>
public sealed class AlertOptions
{
    public const string Section = "Alerts";

    /// <summary>Switch the schedule off (a server that should not run alerts, or tests). "Run now" still works.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The hour in Ethiopia (0-23) from which the day's run may start.</summary>
    public int RunHour { get; set; } = 6;

    /// <summary>How often the schedule looks for rules that have not run today.</summary>
    public int PollSeconds { get; set; } = 600;
}

/// <summary>
/// Runs every enabled alert rule once a day, from <see cref="AlertOptions.RunHour"/> (Ethiopian time). What has run is kept on the rule
/// (<c>LastRunOn</c>), so a restart does not run it again and several servers share the work: each rule is locked while it runs and a server
/// that finds it held leaves it.
/// </summary>
internal sealed partial class AlertScheduler(IServiceScopeFactory scopes, AlertOptions options, TimeProvider clock, ILogger<AlertScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Max(5, options.PollSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(delay, clock, stoppingToken).ConfigureAwait(false);
                await RunDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // keep the schedule going after a failed round (database down)
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogRoundFailed(ex);
            }
        }
    }

    /// <summary>One round: runs the rules that are due, each in a scope of its own. Returns how many ran.</summary>
    internal async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(3));
        if (now.Hour < options.RunHour)
        {
            return 0;
        }

        var today = DateOnly.FromDateTime(now.DateTime);
        IReadOnlyList<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            due = await scope.ServiceProvider.GetRequiredService<IAlertRuleRepository>().ListDueAsync(today, cancellationToken).ConfigureAwait(false);
        }

        var ran = 0;
        foreach (var id in due)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<AlertRunner>().RunAsync(id, today, manual: false, cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess && !result.Value.Skipped)
                {
                    ran++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // one rule failing must not stop the others
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogRuleFailed(ex, id);
            }
        }

        return ran;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The alert schedule failed; it will try again.")]
    private partial void LogRoundFailed(Exception ex);

    [LoggerMessage(Level = LogLevel.Error, Message = "Alert rule {RuleId} failed to run.")]
    private partial void LogRuleFailed(Exception ex, Guid ruleId);
}
