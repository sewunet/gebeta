using Gebeta.Cash.Application;
using Gebeta.Cash.Domain.Cashiers;
using Gebeta.Modularity;
using Gebeta.Persistence;
using Gebeta.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Cash.Infrastructure.Persistence;

/// <summary>Cash Management's database: PostgreSQL schema <c>cash</c>, with its own migration history.</summary>
public sealed class CashDbContext(DbContextOptions<CashDbContext> options, TimeProvider? clock = null) : ModuleDbContext(options, clock), ICashUnitOfWork
{
    public const string SchemaName = "cash";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<Cashier> Cashiers => Set<Cashier>();
    public DbSet<CashLedgerEntry> Ledger => Set<CashLedgerEntry>();
    public DbSet<CashSettings> Settings => Set<CashSettings>();
    public DbSet<PettyCashRequest> PettyCashRequests => Set<PettyCashRequest>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, SchemaName))
            .UseSnakeCaseNamingConvention();
}

internal sealed class CashierConfiguration : IEntityTypeConfiguration<Cashier>
{
    public void Configure(EntityTypeBuilder<Cashier> builder)
    {
        builder.ToTable("cashier");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(140).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.CashAccountCode).HasMaxLength(20).IsRequired();
        foreach (var money in new[] { nameof(Cashier.Balance), nameof(Cashier.MaxTransaction), nameof(Cashier.MaxWithdrawal), nameof(Cashier.MaxDeposit) })
        {
            builder.Property(money).HasPrecision(21, 2);
        }

        // PostgreSQL row version: two movements cannot change the same cashier's balance at once.
        builder.Property<uint>("Version").IsRowVersion();
        builder.Property(c => c.CreatedBy).HasMaxLength(140);
        builder.Property(c => c.ModifiedBy).HasMaxLength(140);
        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class CashLedgerConfiguration : IEntityTypeConfiguration<CashLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CashLedgerEntry> builder)
    {
        builder.ToTable("cash_ledger_entry");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Amount).HasPrecision(21, 2);
        builder.Property(e => e.BalanceAfter).HasPrecision(21, 2);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.SourceNumber).HasMaxLength(60).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(300).IsRequired();
        builder.HasIndex(e => new { e.CashierId, e.Date });
        builder.Property(e => e.CreatedBy).HasMaxLength(140);
        builder.Property(e => e.ModifiedBy).HasMaxLength(140);
        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class CashSettingsConfiguration : IEntityTypeConfiguration<CashSettings>
{
    public void Configure(EntityTypeBuilder<CashSettings> builder)
    {
        builder.ToTable("cash_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => s.CompanyId).IsUnique();
        builder.Property(s => s.MinimumBalance).HasPrecision(21, 2);
        builder.Property(s => s.TargetBalance).HasPrecision(21, 2);
        builder.Property(s => s.DefaultCashAccountCode).HasMaxLength(20);
        builder.Property(s => s.DefaultSourceAccountCode).HasMaxLength(20);
        builder.Property(s => s.CreatedBy).HasMaxLength(140);
        builder.Property(s => s.ModifiedBy).HasMaxLength(140);
        builder.Ignore(s => s.DomainEvents);
    }
}

internal sealed class PettyCashConfiguration : IEntityTypeConfiguration<PettyCashRequest>
{
    public void Configure(EntityTypeBuilder<PettyCashRequest> builder)
    {
        builder.ToTable("petty_cash_request");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(60);
        builder.HasIndex(r => new { r.CompanyId, r.Name }).IsUnique();
        builder.HasIndex(r => new { r.CashierId, r.Status });
        builder.Property(r => r.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FundingFrom).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.CashierName).HasMaxLength(140).IsRequired();
        builder.Property(r => r.RequestedAmount).HasPrecision(21, 2);
        builder.Property(r => r.BalanceWhenRequested).HasPrecision(21, 2);
        builder.Property(r => r.SourceAccountCode).HasMaxLength(20);
        builder.Property(r => r.ApprovedBy).HasMaxLength(140);
        builder.Property(r => r.RejectionReason).HasMaxLength(500);
        builder.Property(r => r.Remarks).HasMaxLength(500);
        builder.Ignore(r => r.IsOpen);
        builder.Ignore(r => r.DocumentType);
        builder.Property(r => r.CreatedBy).HasMaxLength(140);
        builder.Property(r => r.ModifiedBy).HasMaxLength(140);
        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class CashRepository(CashDbContext db) : ICashRepository
{
    public Task<Cashier?> GetCashierAsync(Guid id, CancellationToken cancellationToken) => db.Cashiers.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Cashier>> ListCashiersAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Cashiers.AsNoTracking().Where(c => c.CompanyId == companyId).OrderBy(c => c.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> CashierCodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken) =>
        db.Cashiers.AnyAsync(c => c.CompanyId == companyId && c.Code == code, cancellationToken);

    public async Task<IReadOnlyList<CashLedgerEntry>> LedgerAsync(Guid cashierId, CancellationToken cancellationToken) =>
        await db.Ledger.AsNoTracking().Where(e => e.CashierId == cashierId).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<CashSettings?> GetSettingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        db.Settings.FirstOrDefaultAsync(s => s.CompanyId == companyId, cancellationToken);

    public Task<PettyCashRequest?> GetRequestAsync(Guid id, CancellationToken cancellationToken) => db.PettyCashRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PettyCashRequest>> ListRequestsAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.PettyCashRequests.AsNoTracking().Where(r => r.CompanyId == companyId).OrderByDescending(r => r.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<bool> HasOpenRequestAsync(Guid cashierId, CancellationToken cancellationToken) =>
        await db.PettyCashRequests.AnyAsync(r => r.CashierId == cashierId && (r.Status == PettyCashStatus.Draft || r.Status == PettyCashStatus.Pending), cancellationToken).ConfigureAwait(false)
        || db.ChangeTracker.Entries<PettyCashRequest>().Any(e => e.State == EntityState.Added && e.Entity.CashierId == cashierId && e.Entity.IsOpen);

    public void Add(Cashier cashier) => db.Cashiers.Add(cashier);

    public void Add(CashLedgerEntry entry)
    {
        entry.CreatedAt = DateTimeOffset.UtcNow;
        db.Ledger.Add(entry);
    }

    public void Add(CashSettings settings) => db.Settings.Add(settings);

    public void Add(PettyCashRequest request) => db.PettyCashRequests.Add(request);
}

internal sealed class CashNumbers(CashDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), ICashNumbers;

internal sealed class CashMigrator(CashDbContext db) : IModuleMigrator
{
    public string ModuleId => CashModule.Id;

    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}

internal sealed class CashDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CashDbContext>
{
    public CashDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CashDbContext>();
        CashDbContext.Configure(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new CashDbContext(options.Options);
    }
}
