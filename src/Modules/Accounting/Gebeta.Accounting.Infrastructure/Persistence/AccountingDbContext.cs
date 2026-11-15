using Gebeta.Accounting.Application;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Modularity;
using Gebeta.Persistence;
using Gebeta.SharedKernel;
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

    /// <summary>
    /// The lock date, enforced where every ledger row is written (manual journals, invoices, payments, and
    /// journals from Payroll and Cash): no row may be dated on or before the end of the latest closed month.
    /// A journal from another module into a closed month fails its message, which is retried and dead-lettered.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var posting = ChangeTracker.Entries<GlEntry>().Where(e => e.State == EntityState.Added)
            .GroupBy(e => e.Entity.CompanyId).Select(g => (Company: g.Key, Earliest: g.Min(e => e.Entity.PostingDate))).ToList();
        foreach (var (company, earliest) in posting)
        {
            var closed = await Set<PeriodClosing>().AsNoTracking().Where(p => p.CompanyId == company && p.Status == PeriodClosingStatus.Closed)
                .OrderByDescending(p => p.End).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            if (closed is not null && earliest <= closed.End)
            {
                throw new Gebeta.SharedKernel.DomainException("accounting.period_closed",
                    $"The books are closed up to {closed.End:yyyy-MM-dd} ({closed.Label}); nothing can be posted on {earliest:yyyy-MM-dd}. Reopen the month first.");
            }
        }

        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

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

    public async Task<IReadOnlyList<(Guid AccountId, decimal Debit, decimal Credit)>> TotalsBetweenAsync(Guid companyId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken, bool excludeYearEnd = false) =>
        (await db.GlEntries.AsNoTracking().Where(g => g.CompanyId == companyId && g.PostingDate >= from && g.PostingDate <= to)
            .Where(g => !excludeYearEnd || !db.Journals.Any(j => j.Id == g.VoucherId && j.Source == JournalSource.YearEnd))
            .GroupBy(g => g.AccountId)
            .Select(g => new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
            .ToListAsync(cancellationToken).ConfigureAwait(false))
        .Select(x => (x.AccountId, x.Debit, x.Credit)).ToList();

    public Task<JournalEntry?> FindSubmittedAsync(Guid companyId, JournalSource source, DateOnly postingDate, CancellationToken cancellationToken) =>
        db.Journals.FirstOrDefaultAsync(j => j.CompanyId == companyId && j.Source == source && j.PostingDate == postingDate && j.DocStatus == DocStatus.Submitted,
            cancellationToken);

    public async Task<IReadOnlyList<GlEntry>> EntriesAsync(Guid companyId, Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.GlEntries.AsNoTracking().Where(g => g.CompanyId == companyId && g.AccountId == accountId && g.PostingDate >= from && g.PostingDate <= to)
            .OrderBy(g => g.PostingDate).ThenBy(g => g.CreatedAt).ThenBy(g => g.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class PeriodClosingConfiguration : IEntityTypeConfiguration<PeriodClosing>
{
    public void Configure(EntityTypeBuilder<PeriodClosing> builder)
    {
        builder.ToTable("period_closing");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => new { p.CompanyId, p.End });
        builder.Property(p => p.Label).HasMaxLength(40).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.TotalDebit).HasPrecision(21, 2);
        builder.Property(p => p.TotalCredit).HasPrecision(21, 2);
        builder.Property(p => p.NetProfit).HasPrecision(21, 2);
        builder.Property(p => p.ClosedBy).HasMaxLength(140).IsRequired();
        builder.Property(p => p.ReopenedBy).HasMaxLength(140);
        builder.Property(p => p.ReopenReason).HasMaxLength(300);
        builder.Property(p => p.CreatedBy).HasMaxLength(140);
        builder.Property(p => p.ModifiedBy).HasMaxLength(140);
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class PeriodClosingRepository(AccountingDbContext db) : IPeriodClosingRepository
{
    public Task<PeriodClosing?> LatestClosedAsync(Guid companyId, CancellationToken cancellationToken) =>
        db.Set<PeriodClosing>().Where(p => p.CompanyId == companyId && p.Status == PeriodClosingStatus.Closed)
            .OrderByDescending(p => p.End).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PeriodClosing>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Set<PeriodClosing>().AsNoTracking().Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.End).ThenByDescending(p => p.ClosedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(PeriodClosing closing) => db.Set<PeriodClosing>().Add(closing);
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
        builder.Property(i => i.ReturnAgainstNumber).HasMaxLength(60);
        builder.Property(i => i.WithholdingReceiptNumber).HasMaxLength(60);
        builder.Property(i => i.WithholdingBase).HasPrecision(21, 2);
        builder.Property(i => i.WithholdingRatePercent).HasPrecision(5, 2);
        builder.Ignore(i => i.SupplyType);
        builder.Ignore(i => i.NeedsWithholdingReceipt);
        builder.HasIndex(i => i.ReturnAgainstId).HasFilter("return_against_id IS NOT NULL");
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
        builder.Property(p => p.Kind).HasConversion<string>().HasMaxLength(20);
        builder.OwnsMany<Gebeta.Accounting.Domain.Invoicing.PaymentAllocation>("_allocations", a =>
        {
            a.ToTable("payment_allocation");
            a.WithOwner().HasForeignKey("PaymentId");
            a.Property<int>("Id");
            a.HasKey("PaymentId", "Id");
            a.Property(x => x.InvoiceNumber).HasMaxLength(60);
            a.Property(x => x.Amount).HasPrecision(21, 2);
            a.HasIndex(x => x.InvoiceId);
        });
        builder.Ignore(p => p.Allocations);
        builder.Property(p => p.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PartyName).HasMaxLength(140).IsRequired();
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

    public Task<Gebeta.Accounting.Domain.Invoicing.Payment?> GetPaymentAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Gebeta.Accounting.Domain.Invoicing.Payment>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Invoicing.Payment>> PaymentsForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Invoicing.Payment>().AsNoTracking()
            .Where(p => EF.Property<List<Gebeta.Accounting.Domain.Invoicing.PaymentAllocation>>(p, "_allocations").Any(a => a.InvoiceId == invoiceId))
            .OrderBy(p => p.PostingDate).ThenBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Invoicing.Invoice>> ReturnsAgainstAsync(Guid invoiceId, CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Invoicing.Invoice>().AsNoTracking().Where(i => i.ReturnAgainstId == invoiceId)
            .OrderBy(i => i.PostingDate).ThenBy(i => i.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Gebeta.Accounting.Domain.Invoicing.Invoice invoice) => db.Set<Gebeta.Accounting.Domain.Invoicing.Invoice>().Add(invoice);

    public void Add(Gebeta.Accounting.Domain.Invoicing.Payment payment) => db.Set<Gebeta.Accounting.Domain.Invoicing.Payment>().Add(payment);
}

internal sealed class BankAccountConfiguration : IEntityTypeConfiguration<Gebeta.Accounting.Domain.Banking.BankAccount>
{
    public void Configure(EntityTypeBuilder<Gebeta.Accounting.Domain.Banking.BankAccount> builder)
    {
        builder.ToTable("bank_account");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Name).HasMaxLength(140).IsRequired();
        builder.Property(b => b.BankName).HasMaxLength(140).IsRequired();
        builder.Property(b => b.AccountNumber).HasMaxLength(40).IsRequired();
        builder.Property(b => b.LedgerAccountCode).HasMaxLength(20).IsRequired();
        builder.HasIndex(b => b.LedgerAccountId).IsUnique();
        builder.HasIndex(b => b.CompanyId);
        builder.Property(b => b.CreatedBy).HasMaxLength(140);
        builder.Property(b => b.ModifiedBy).HasMaxLength(140);
        builder.Ignore(b => b.DomainEvents);
    }
}

internal sealed class BankStatementLineConfiguration : IEntityTypeConfiguration<Gebeta.Accounting.Domain.Banking.BankStatementLine>
{
    public void Configure(EntityTypeBuilder<Gebeta.Accounting.Domain.Banking.BankStatementLine> builder)
    {
        builder.ToTable("bank_statement_line");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.HasIndex(l => new { l.BankAccountId, l.Date });
        builder.Property(l => l.Description).HasMaxLength(300).IsRequired();
        builder.Property(l => l.Reference).HasMaxLength(100);
        builder.Property(l => l.Amount).HasPrecision(21, 2);
        builder.Property(l => l.Balance).HasPrecision(21, 2);
        builder.Property(l => l.CreatedBy).HasMaxLength(140);
        builder.Property(l => l.ModifiedBy).HasMaxLength(140);
        builder.Ignore(l => l.DomainEvents);
    }
}

internal sealed class BankMatchConfiguration : IEntityTypeConfiguration<Gebeta.Accounting.Domain.Banking.BankMatch>
{
    public void Configure(EntityTypeBuilder<Gebeta.Accounting.Domain.Banking.BankMatch> builder)
    {
        builder.ToTable("bank_match");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.HasIndex(m => m.GlEntryId).IsUnique(); // a ledger row is matched to at most one statement line
        builder.HasIndex(m => new { m.BankAccountId, m.StatementLineId });
        builder.Property(m => m.Amount).HasPrecision(21, 2);
        builder.Property(m => m.CreatedBy).HasMaxLength(140);
        builder.Property(m => m.ModifiedBy).HasMaxLength(140);
        builder.Ignore(m => m.DomainEvents);
    }
}

internal sealed class BankRepository(AccountingDbContext db) : Gebeta.Accounting.Domain.Banking.IBankRepository
{
    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Banking.BankAccount>> ListAccountsAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Banking.BankAccount>().AsNoTracking().Where(b => b.CompanyId == companyId).OrderBy(b => b.Name)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Gebeta.Accounting.Domain.Banking.BankAccount?> GetAccountAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Gebeta.Accounting.Domain.Banking.BankAccount>().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public void Add(Gebeta.Accounting.Domain.Banking.BankAccount account) => db.Set<Gebeta.Accounting.Domain.Banking.BankAccount>().Add(account);

    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Banking.BankStatementLine>> LinesAsync(Guid bankAccountId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Banking.BankStatementLine>().AsNoTracking()
            .Where(l => l.BankAccountId == bankAccountId && l.Date >= from && l.Date <= to)
            .OrderBy(l => l.Date).ThenBy(l => l.CreatedAt).ThenBy(l => l.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Gebeta.Accounting.Domain.Banking.BankStatementLine?> GetLineAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<Gebeta.Accounting.Domain.Banking.BankStatementLine>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public void Add(IEnumerable<Gebeta.Accounting.Domain.Banking.BankStatementLine> lines) => db.Set<Gebeta.Accounting.Domain.Banking.BankStatementLine>().AddRange(lines);

    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Banking.BankMatch>> MatchesAsync(Guid bankAccountId, CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Banking.BankMatch>().Where(m => m.BankAccountId == bankAccountId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Gebeta.Accounting.Domain.Banking.BankMatch>> MatchesForLineAsync(Guid statementLineId, CancellationToken cancellationToken) =>
        await db.Set<Gebeta.Accounting.Domain.Banking.BankMatch>().Where(m => m.StatementLineId == statementLineId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(IEnumerable<Gebeta.Accounting.Domain.Banking.BankMatch> matches) => db.Set<Gebeta.Accounting.Domain.Banking.BankMatch>().AddRange(matches);

    public void Remove(IEnumerable<Gebeta.Accounting.Domain.Banking.BankMatch> matches) => db.Set<Gebeta.Accounting.Domain.Banking.BankMatch>().RemoveRange(matches);
}
