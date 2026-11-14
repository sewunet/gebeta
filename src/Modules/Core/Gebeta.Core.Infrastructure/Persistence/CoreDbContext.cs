using Gebeta.Core.Application.Companies;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Security;
using Gebeta.Core.Domain.Settings;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Core.Infrastructure.Persistence;

/// <summary>Core's database: PostgreSQL schema <c>core</c>, with its own migration history.</summary>
public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options) : DbContext(options), ICoreUnitOfWork
{
    public const string Schema = "core";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<NamingSeries> NamingSeries => Set<NamingSeries>();
    public DbSet<Holiday> Holidays => Set<Holiday>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreDbContext).Assembly);
        modelBuilder.AddNamingSeriesCounters();
    }

    async Task Gebeta.Application.IUnitOfWork.SaveChangesAsync(CancellationToken cancellationToken) =>
        await SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
