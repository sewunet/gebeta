using Gebeta.Application;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;

// A tiny "sample" module used only by these tests: its own schema, outbox, inbox and two consumers.
namespace Gebeta.Sample.Contracts
{
    public sealed record ThingHappened(Guid ThingId, string Name);

    public sealed record ThingBroke(Guid ThingId);

    public sealed record PriceChangedV2(Guid ThingId, string Price);
}

namespace Gebeta.Messaging.Tests
{
    using Gebeta.Sample.Contracts;

    public sealed class SampleDbContext(DbContextOptions<SampleDbContext> options) : DbContext(options)
    {
        public const string Schema = "sample";

        public DbSet<ReceivedThing> Received => Set<ReceivedThing>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasDefaultSchema(Schema);
            modelBuilder.Entity<ReceivedThing>(b =>
            {
                b.ToTable("received_thing");
                b.HasKey(r => r.Id);
                b.Property(r => r.Id).HasColumnName("id").ValueGeneratedOnAdd();
                b.Property(r => r.ThingId).HasColumnName("thing_id");
                b.Property(r => r.Name).HasColumnName("name");
            });
            modelBuilder.AddOutboxAndInbox();
        }
    }

    public sealed class ReceivedThing
    {
        public long Id { get; set; }
        public Guid ThingId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>Records each event in the module's own table, in the inbox transaction.</summary>
    public sealed class RecordThing(SampleDbContext db) : IIntegrationEventConsumer<ThingHappened>
    {
        public Task HandleAsync(ThingHappened integrationEvent, CancellationToken cancellationToken)
        {
            db.Received.Add(new ReceivedThing { ThingId = integrationEvent.ThingId, Name = integrationEvent.Name });
            return Task.CompletedTask;
        }
    }

    /// <summary>Always fails, so its messages end up in the dead-letter queue.</summary>
    public sealed class AlwaysFails : IIntegrationEventConsumer<ThingBroke>
    {
        public Task HandleAsync(ThingBroke integrationEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Cannot handle thing {integrationEvent.ThingId}");
    }
}
