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

internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Gebeta.Accounting.Domain.Invoicing.Invoice>
{
    public void Configure(EntityTypeBuilder<Gebeta.Accounting.Domain.Invoicing.Invoice> builder)
    {
        builder.ToTable("invoice");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Name).HasMaxLength(60);
        builder.HasIndex(i => new { i.CompanyId, i.Name }).IsUnique();
        builder.HasIndex(i => new { i.CompanyId, i.Kind, i.PostingDate });
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.PartyName).HasMaxLength(140).IsRequired();
        builder.Property(i => i.PartyTin).HasMaxLength(10);
        builder.Property(i => i.SupplierInvoiceNumber).HasMaxLength(60);
        foreach (var money in new[] { "NetTotal", "VatAmount", "WithholdingAmount", "Outstanding" })
        {
            builder.Property(money).HasPrecision(21, 2);
        }

        builder.Property<List<Gebeta.Accounting.Domain.Invoicing.InvoiceLine>>("_lines").HasColumnName("lines").IsJsonList();
        builder.Ignore(i => i.Lines);
        builder.Ignore(i => i.GrandTotal);
        builder.Ignore(i => i.PayableAmount);
        builder.Ignore(i => i.DocumentType);
        builder.Property(i => i.CreatedBy).HasMaxLength(140);
        builder.Property(i => i.ModifiedBy).HasMaxLength(140);
        builder.Ignore(i => i.DomainEvents);
    }
}

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Gebeta.Accounting.Domain.Invoicing.Payment>
{
    public void Configure(EntityTypeBuilder<Gebeta.Accounting.Domain.Invoicing.Payment> builder)
    {
        builder.ToTable("payment");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(60);
        builder.HasIndex(p => new { p.CompanyId, p.Name }).IsUnique();
        builder.HasIndex(p => p.InvoiceId);
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PartyName).HasMaxLength(140).IsRequired();
        builder.Property(p => p.InvoiceNumber).HasMaxLength(60);
        builder.Property(p => p.Amount).HasPrecision(21, 2);
        builder.Property(p => p.BankAccountCode).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Reference).HasMaxLength(100);
        builder.Ignore(p => p.DocumentType);
        builder.Property(p => p.CreatedBy).HasMaxLength(140);
        builder.Property(p => p.ModifiedBy).HasMaxLength(140);
        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class InvoiceRepository(AccountingDbContext db) : Gebeta.Accounting.Domain.Invoicing.IInvoiceRepository
{
    public Task<Gebeta.Accounting.Domain.Invoicing.Invoice?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Gebeta.Accounting.Domain.Invoicing.Invoice>().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Invoicing.Invoice>> ListAsync(Guid companyId, Gebeta.Accounting.Domain.Invoicing.InvoiceKind? kind,
        DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Invoicing.Invoice>().AsNoTracking()
            .Where(i => i.CompanyId == companyId && (kind == null || i.Kind == kind) && i.PostingDate >= from && i.PostingDate <= to)
            .OrderBy(i => i.PostingDate).ThenBy(i => i.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Invoicing.Payment>> PaymentsAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Invoicing.Payment>().AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.PostingDate >= from && p.PostingDate <= to).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Gebeta.Accounting.Domain.Invoicing.Invoice invoice) => db.Set<Gebeta.Accounting.Domain.Invoicing.Invoice>().Add(invoice);

    public void Add(Gebeta.Accounting.Domain.Invoicing.Payment payment) => db.Set<Gebeta.Accounting.Domain.Invoicing.Payment>().Add(payment);
}
