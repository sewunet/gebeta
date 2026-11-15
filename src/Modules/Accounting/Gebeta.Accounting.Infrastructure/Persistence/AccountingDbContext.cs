using Gebeta.Accounting.Application;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Modularity;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Accounting.Infrastructure.Persistence;

/// <summary>Accounting's database: PostgreSQL schema <c>accounting</c>, with its own migration history.</summary>
public sealed class AccountingDbContext(DbContextOptions<AccountingDbContext> options, TimeProvider? clock = null)
    : ModuleDbContext(options, clock), IAccountingUnitOfWork
{
    public const string SchemaName = "accounting";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> Journals => Set<JournalEntry>();
    public DbSet<GlEntry> GlEntries => Set<GlEntry>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, SchemaName))
            .UseSnakeCaseNamingConvention();
}

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("account");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Code).HasMaxLength(20).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(140).IsRequired();
        builder.Property(a => a.RootType).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
        builder.Ignore(a => a.IsDebitNormal);
        builder.Property(a => a.CreatedBy).HasMaxLength(140);
        builder.Property(a => a.ModifiedBy).HasMaxLength(140);
        builder.Ignore(a => a.DomainEvents);
    }
}

internal sealed class JournalConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("journal_entry");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Name).HasMaxLength(60);
        builder.HasIndex(j => new { j.CompanyId, j.Name }).IsUnique();
        builder.HasIndex(j => new { j.CompanyId, j.PostingDate });
        builder.HasIndex(j => j.SourceId).IsUnique().HasFilter("source_id IS NOT NULL");
        builder.Property(j => j.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Remark).HasMaxLength(300).IsRequired();
        builder.Property(j => j.SourceNumber).HasMaxLength(60);
        builder.Property<List<JournalLine>>("_lines").HasColumnName("lines").IsJsonList();
        builder.Ignore(j => j.Lines);
        builder.Ignore(j => j.TotalDebit);
        builder.Ignore(j => j.TotalCredit);
        builder.Ignore(j => j.DocumentType);
        builder.Property(j => j.CreatedBy).HasMaxLength(140);
        builder.Property(j => j.ModifiedBy).HasMaxLength(140);
        builder.Ignore(j => j.DomainEvents);
    }
}

internal sealed class GlEntryConfiguration : IEntityTypeConfiguration<GlEntry>
{
    public void Configure(EntityTypeBuilder<GlEntry> builder)
    {
        builder.ToTable("gl_entry");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Debit).HasPrecision(21, 2);
        builder.Property(g => g.Credit).HasPrecision(21, 2);
        builder.Property(g => g.VoucherNumber).HasMaxLength(60).IsRequired();
        builder.Property(g => g.Description).HasMaxLength(300).IsRequired();
        builder.HasIndex(g => new { g.CompanyId, g.PostingDate });
        builder.HasIndex(g => new { g.AccountId, g.PostingDate });
        builder.HasIndex(g => g.VoucherId);
        builder.Property(g => g.CreatedBy).HasMaxLength(140);
        builder.Property(g => g.ModifiedBy).HasMaxLength(140);
        builder.Ignore(g => g.DomainEvents);
    }
}

internal sealed class AccountRepository(AccountingDbContext db) : IAccountRepository
{
    public async Task<IReadOnlyList<Account>> ListAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // Accounts added in this unit of work (chart just set up) are visible too.
        var saved = await db.Accounts.AsNoTracking().Where(a => a.CompanyId == companyId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var pending = db.ChangeTracker.Entries<Account>().Where(e => e.State == EntityState.Added && e.Entity.CompanyId == companyId).Select(e => e.Entity);
        return saved.Concat(pending).DistinctBy(a => a.Id).OrderBy(a => a.Code, StringComparer.Ordinal).ToList();
    }

    public Task<bool> AnyAsync(Guid companyId, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.CompanyId == companyId, cancellationToken);

    public void Add(Account account) => db.Accounts.Add(account);
}

internal sealed class JournalRepository(AccountingDbContext db) : IJournalRepository
{
    public Task<JournalEntry?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Journals.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task<JournalEntry?> FindBySourceAsync(Guid sourceId, CancellationToken cancellationToken) =>
        db.Journals.FirstOrDefaultAsync(j => j.SourceId == sourceId, cancellationToken);

    public async Task<IReadOnlyList<JournalEntry>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Journals.AsNoTracking().Where(j => j.CompanyId == companyId && j.PostingDate >= from && j.PostingDate <= to)
            .OrderBy(j => j.PostingDate).ThenBy(j => j.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(JournalEntry journal) => db.Journals.Add(journal);

    public void Add(IEnumerable<GlEntry> entries) => db.GlEntries.AddRange(entries);

    public async Task<IReadOnlyList<(Guid AccountId, decimal Debit, decimal Credit)>> TotalsAsync(Guid companyId, DateOnly asOf, CancellationToken cancellationToken) =>
        (await db.GlEntries.AsNoTracking().Where(g => g.CompanyId == companyId && g.PostingDate <= asOf)
            .GroupBy(g => g.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .Select(x => (x.AccountId, x.Debit, x.Credit)).ToList();
}

internal sealed class AccountingNumbers(AccountingDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), IAccountingNumbers;

internal sealed class AccountingMigrator(AccountingDbContext db) : IModuleMigrator
{
    public string ModuleId => AccountingModule.Id;

    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}

internal sealed class AccountingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AccountingDbContext>
{
    public AccountingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AccountingDbContext>();
        AccountingDbContext.Configure(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new AccountingDbContext(options.Options);
    }
}
