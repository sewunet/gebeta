using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Persistence;

/// <summary>
/// An integration event waiting to be sent to RabbitMQ. It is saved in the module's own schema in the
/// same transaction as the change that caused it, so an event is never lost and never sent for a change
/// that was rolled back (docs/messaging.md).
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string RoutingKey { get; set; } = string.Empty;
    public string ClrType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

/// <summary>A message a module has already handled, so a repeated delivery is skipped.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Handler { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}

public static class MessagingModel
{
    public const string OutboxTable = "outbox_message";
    public const string InboxTable = "inbox_message";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Adds the outbox and inbox tables to a module's model (and so to its migrations).</summary>
    public static ModelBuilder AddOutboxAndInbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable(OutboxTable);
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.RoutingKey).HasColumnName("routing_key").HasMaxLength(200).IsRequired();
            b.Property(m => m.ClrType).HasColumnName("clr_type").HasMaxLength(300).IsRequired();
            b.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
            b.Property(m => m.OccurredAt).HasColumnName("occurred_at");
            b.Property(m => m.ProcessedAt).HasColumnName("processed_at");
            b.Property(m => m.Attempts).HasColumnName("attempts");
            b.Property(m => m.LastError).HasColumnName("last_error").HasMaxLength(2000);
            b.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL").HasDatabaseName("ix_outbox_message_pending");
        });
        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable(InboxTable);
            b.HasKey(m => new { m.MessageId, m.Handler });
            b.Property(m => m.MessageId).HasColumnName("message_id");
            b.Property(m => m.Handler).HasColumnName("handler").HasMaxLength(300);
            b.Property(m => m.ProcessedAt).HasColumnName("processed_at");
        });
        return modelBuilder;
    }

    /// <summary>Queues <paramref name="integrationEvent"/> in the module's outbox; it is saved with the next SaveChanges.</summary>
    public static void AddIntegrationEvent(this DbContext db, object integrationEvent, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        var type = integrationEvent.GetType();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            RoutingKey = EventNaming.RoutingKeyFor(type),
            ClrType = type.AssemblyQualifiedName ?? type.FullName!,
            Payload = JsonSerializer.Serialize(integrationEvent, type, Json),
            OccurredAt = now,
        });
    }
}

/// <summary>
/// Routing keys follow docs/messaging.md: <c>&lt;module&gt;.&lt;event&gt;.v&lt;n&gt;</c>, derived from the
/// contract type: <c>Gebeta.Payroll.Contracts.SalarySlipSubmitted</c> → <c>payroll.salary-slip-submitted.v1</c>,
/// and a name ending in V2 → <c>.v2</c>. Contracts therefore need no attributes or dependencies.
/// </summary>
public static class EventNaming
{
    public static string RoutingKeyFor(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        var ns = eventType.Namespace?.Split('.') ?? [];
        if (ns.Length < 3 || ns[0] != "Gebeta" || ns[^1] != "Contracts")
        {
            throw new InvalidOperationException($"Integration event {eventType.FullName} must live in a Gebeta.<Module>.Contracts namespace.");
        }

        var name = eventType.Name;
        var version = 1;
        var vIndex = name.LastIndexOf('V');
        if (vIndex > 0 && vIndex < name.Length - 1 && name[(vIndex + 1)..].All(char.IsAsciiDigit))
        {
            version = int.Parse(name[(vIndex + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            name = name[..vIndex];
        }

        return $"{Kebab(ns[1])}.{Kebab(name)}.v{version}";
    }

    private static string Kebab(string pascal)
    {
        var chars = new List<char>(pascal.Length + 8);
        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && (char.IsLower(pascal[i - 1]) || (i + 1 < pascal.Length && char.IsLower(pascal[i + 1]))))
                {
                    chars.Add('-');
                }

                chars.Add(char.ToLowerInvariant(c));
            }
            else
            {
                chars.Add(c);
            }
        }

        return new string([.. chars]);
    }
}
