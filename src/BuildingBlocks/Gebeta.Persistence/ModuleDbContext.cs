using Gebeta.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gebeta.Persistence;

/// <summary>
/// Base for every module's DbContext: its own schema, outbox and inbox, document number counters,
/// and the <see cref="IUnitOfWork"/> operations the Application layer uses.
/// </summary>
public abstract class ModuleDbContext(DbContextOptions options, TimeProvider? clock) : DbContext(options), IUnitOfWork
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public abstract string Schema { get; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(GetType().Assembly, t => IsOwnConfiguration(t));
        modelBuilder.AddNamingSeriesCounters();
        modelBuilder.AddOutboxAndInbox();
        modelBuilder.AddChangeHistory();
    }

    /// <summary>Saves, writing the change history of audited records (<see cref="Gebeta.SharedKernel.IAudited"/>) in the same save.</summary>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RecordHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RecordHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void RecordHistory()
    {
        if (!ChangeTracker.Entries().Any(e => e.Entity is Gebeta.SharedKernel.IAudited))
        {
            return;
        }

        var rows = ChangeHistoryModel.Collect(ChangeTracker, ChangeHistoryModel.UserOf(this), _clock.GetUtcNow());
        if (rows.Count > 0)
        {
            Set<AuditChange>().AddRange(rows);
        }
    }

    /// <summary>Only configurations written for this context (a module may have one context per assembly).</summary>
    protected virtual bool IsOwnConfiguration(Type configurationType) => true;

    public void Publish(object integrationEvent) => this.AddIntegrationEvent(integrationEvent, _clock.GetUtcNow());

    async Task IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new TransactionScope(await Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false));

    private sealed class TransactionScope(IDbContextTransaction transaction) : ITransactionScope
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}

/// <summary>A module's <see cref="IDocumentNumbers"/>, using its own DbContext and counter table.</summary>
public abstract class ModuleDocumentNumbers(ModuleDbContext db, DocumentNumbering numbering) : IDocumentNumbers
{
    public Task<string> NextAsync(Guid companyId, string documentType, string defaultPattern, DateOnly date, CancellationToken cancellationToken) =>
        numbering.NextAsync(db, db.Schema, companyId, documentType, defaultPattern, date, cancellationToken);
}
