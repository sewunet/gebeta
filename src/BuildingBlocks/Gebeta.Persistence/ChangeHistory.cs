using System.Collections;
using System.Globalization;
using Gebeta.Application;
using Gebeta.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Gebeta.Persistence;

/// <summary>A row of a module's change history (table <c>audit_change</c> in the module's schema). Written with the change, in the same save.</summary>
public sealed class AuditChange
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid EntityId { get; init; }
    public string EntityType { get; init; } = string.Empty;
    public Guid? CompanyId { get; init; }
    public DateTimeOffset ChangedAt { get; init; }
    public string ChangedBy { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string? Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
}

public static class ChangeHistoryModel
{
    public const string Table = "audit_change";
    private const int MaxValue = 500;

    /// <summary>Bookkeeping fields every entity has; their changes are the change itself, not news.</summary>
    private static readonly HashSet<string> Ignored = [nameof(Entity.CreatedAt), nameof(Entity.CreatedBy), nameof(Entity.ModifiedAt), nameof(Entity.ModifiedBy)];

    public static ModelBuilder AddChangeHistory(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<AuditChange>(b =>
        {
            b.ToTable(Table);
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(c => c.EntityId).HasColumnName("entity_id");
            b.Property(c => c.EntityType).HasColumnName("entity_type").HasMaxLength(100).IsRequired();
            b.Property(c => c.CompanyId).HasColumnName("company_id");
            b.Property(c => c.ChangedAt).HasColumnName("changed_at");
            b.Property(c => c.ChangedBy).HasColumnName("changed_by").HasMaxLength(140).IsRequired();
            b.Property(c => c.Action).HasColumnName("action").HasMaxLength(20).IsRequired();
            b.Property(c => c.Field).HasColumnName("field").HasMaxLength(100);
            b.Property(c => c.OldValue).HasColumnName("old_value").HasMaxLength(MaxValue);
            b.Property(c => c.NewValue).HasColumnName("new_value").HasMaxLength(MaxValue);
            b.HasIndex(c => new { c.EntityId, c.ChangedAt }).HasDatabaseName("ix_audit_change_entity");
        });
        return modelBuilder;
    }

    /// <summary>The history rows for the audited entities about to be saved: one per changed field, or one for a record created or deleted.</summary>
    internal static List<AuditChange> Collect(ChangeTracker tracker, string user, DateTimeOffset now)
    {
        var rows = new List<AuditChange>();
        foreach (var entry in tracker.Entries().Where(e => e.Entity is IAudited && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList())
        {
            var id = ((IAudited)entry.Entity).HistoryOwnerId ?? (entry.Property(nameof(Entity.Id)).CurrentValue is Guid g ? g : Guid.Empty);
            var type = entry.Metadata.ClrType.Name;
            var company = entry.Metadata.FindProperty("CompanyId") is not null ? entry.Property("CompanyId").CurrentValue as Guid? : type == "Company" ? id : null;
            AuditChange Row(string action, string? field = null, string? oldValue = null, string? newValue = null) => new()
            {
                EntityId = id, EntityType = type, CompanyId = company, ChangedAt = now, ChangedBy = user, Action = action, Field = field, OldValue = oldValue, NewValue = newValue,
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    rows.Add(Row("Created"));
                    break;
                case EntityState.Deleted:
                    rows.Add(Row("Deleted"));
                    break;
                default:
                    foreach (var property in entry.Properties.Where(p => p.IsModified && !Ignored.Contains(p.Metadata.Name)))
                    {
                        if (Equals(property.OriginalValue, property.CurrentValue)
                            || (property.Metadata.GetValueComparer() is { } comparer && comparer.Equals(property.OriginalValue, property.CurrentValue)))
                        {
                            continue;
                        }

                        var field = property.Metadata.Name.TrimStart('_');
                        rows.Add(property.CurrentValue is IEnumerable and not string
                            ? Row("Changed", field) // a list (lines, allocations): the document itself shows it
                            : Row("Changed", field, Format(property.OriginalValue), Format(property.CurrentValue)));
                    }

                    break;
            }
        }

        return rows;
    }

    private static string? Format(object? value)
    {
        var text = value switch
        {
            null => null,
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset t => t.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
        return text is { Length: > MaxValue } ? text[..MaxValue] : text;
    }

    /// <summary>Who is saving: the signed-in user, or "system" for background work (event consumers, the scheduler).</summary>
    internal static string UserOf(DbContext context)
    {
        try
        {
            var user = context.GetService<ICurrentUser>();
            return user.IsAuthenticated && !string.IsNullOrWhiteSpace(user.Username) ? user.Username : "system";
        }
        catch (InvalidOperationException)
        {
            return "system"; // no application services (tools, tests)
        }
    }
}

/// <summary>A module's <see cref="IChangeHistory"/>, read from its own <c>audit_change</c> table.</summary>
public sealed class ModuleChangeHistory<TContext>(TContext db) : IChangeHistory
    where TContext : ModuleDbContext
{
    public async Task<IReadOnlyList<ChangeRecord>> ForAsync(Guid entityId, CancellationToken cancellationToken) =>
        (await db.Set<AuditChange>().AsNoTracking().Where(c => c.EntityId == entityId).OrderByDescending(c => c.ChangedAt).ThenBy(c => c.Field)
            .Take(500).ToListAsync(cancellationToken).ConfigureAwait(false))
        .Select(c => new ChangeRecord(c.EntityId, c.EntityType, c.CompanyId, c.ChangedAt, c.ChangedBy, c.Action, c.Field, c.OldValue, c.NewValue)).ToList();
}
