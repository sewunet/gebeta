using Gebeta.Modularity;
using Gebeta.Persistence;
using Gebeta.Stock.Application;
using Gebeta.Stock.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Stock.Infrastructure.Persistence;

/// <summary>Stock's database: PostgreSQL schema <c>stock</c>, with its own migration history.</summary>
public sealed class StockDbContext(DbContextOptions<StockDbContext> options, TimeProvider? clock = null) : ModuleDbContext(options, clock), IStockUnitOfWork
{
    public const string SchemaName = "stock";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StockItemSetting> ItemSettings => Set<StockItemSetting>();
    public DbSet<StockEntry> Entries => Set<StockEntry>();
    public DbSet<StockLedgerEntry> Ledger => Set<StockLedgerEntry>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, SchemaName))
            .UseSnakeCaseNamingConvention();
}

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouse");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(w => new { w.CompanyId, w.Code }).IsUnique();
        builder.Property(w => w.Name).HasMaxLength(140).IsRequired();
        builder.Property(w => w.StockAccountCode).HasMaxLength(20).IsRequired();
        builder.Property(w => w.CreatedBy).HasMaxLength(140);
        builder.Property(w => w.ModifiedBy).HasMaxLength(140);
        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class StockItemSettingConfiguration : IEntityTypeConfiguration<StockItemSetting>
{
    public void Configure(EntityTypeBuilder<StockItemSetting> builder)
    {
        builder.ToTable("item_setting");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => new { s.CompanyId, s.ItemId }).IsUnique();
        builder.Property(s => s.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.CreatedBy).HasMaxLength(140);
        builder.Property(s => s.ModifiedBy).HasMaxLength(140);
        builder.Ignore(s => s.DomainEvents);
    }
}

internal sealed class StockEntryConfiguration : IEntityTypeConfiguration<StockEntry>
{
    public void Configure(EntityTypeBuilder<StockEntry> builder)
    {
        builder.ToTable("stock_entry");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(60);
        builder.HasIndex(e => new { e.CompanyId, e.Name }).IsUnique();
        builder.HasIndex(e => new { e.CompanyId, e.PostingDate });
        builder.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.CounterAccountCode).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Remark).HasMaxLength(300);
        builder.Property(e => e.PartyName).HasMaxLength(140);
        builder.Property(e => e.Reference).HasMaxLength(60);
        builder.HasIndex(e => e.PartyId).HasFilter("party_id IS NOT NULL");
        builder.Property<List<StockEntryLine>>("_lines").HasColumnName("lines").IsJsonList();
        builder.Ignore(e => e.Lines);
        builder.Ignore(e => e.TotalValue);
        builder.Ignore(e => e.DocumentType);
        builder.Property(e => e.CreatedBy).HasMaxLength(140);
        builder.Property(e => e.ModifiedBy).HasMaxLength(140);
        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class StockLedgerEntryConfiguration : IEntityTypeConfiguration<StockLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StockLedgerEntry> builder)
    {
        builder.ToTable("stock_ledger_entry");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Sequence).UseIdentityAlwaysColumn();
        builder.HasIndex(e => new { e.ItemId, e.WarehouseId, e.Sequence });
        builder.HasIndex(e => new { e.CompanyId, e.PostingDate });
        builder.HasIndex(e => e.VoucherId);
        builder.Property(e => e.ActualQty).HasPrecision(18, 3);
        builder.Property(e => e.QtyAfter).HasPrecision(18, 3);
        builder.Property(e => e.ValueChange).HasPrecision(21, 2);
        builder.Property(e => e.ValueAfter).HasPrecision(21, 2);
        builder.Property(e => e.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(e => new { e.ItemId, e.WarehouseId, e.PostingDate, e.Sequence });
        builder.Property(e => e.VoucherNumber).HasMaxLength(60).IsRequired();
        builder.Property<List<FifoLayer>>("_queue").HasColumnName("fifo_queue").IsJsonList();
        builder.Ignore(e => e.Queue);
        builder.Ignore(e => e.Rate);
        builder.Ignore(e => e.State);
        builder.Property(e => e.CreatedBy).HasMaxLength(140);
        builder.Property(e => e.ModifiedBy).HasMaxLength(140);
        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class StockRepository(StockDbContext db) : IStockRepository
{
    public async Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Warehouses.AsNoTracking().Where(w => w.CompanyId == companyId).OrderBy(w => w.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Warehouse?> GetWarehouseAsync(Guid id, CancellationToken cancellationToken) => db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public void Add(Warehouse warehouse) => db.Warehouses.Add(warehouse);

    public async Task<IReadOnlyDictionary<Guid, StockItemSetting>> SettingsAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.ItemSettings.Where(s => s.CompanyId == companyId).ToDictionaryAsync(s => s.ItemId, cancellationToken).ConfigureAwait(false);

    public void Add(StockItemSetting setting) => db.ItemSettings.Add(setting);

    public async Task LockAsync(IEnumerable<(Guid Item, Guid Warehouse)> pairs, CancellationToken cancellationToken)
    {
        // Advisory locks held until the transaction ends; taken in a fixed order so two postings cannot deadlock.
        foreach (var key in pairs.Select(p => LockKey(p.Item, p.Warehouse)).Distinct().Order())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", cancellationToken).ConfigureAwait(false);
        }
    }

    private static long LockKey(Guid item, Guid warehouse)
    {
        var a = item.ToByteArray();
        var b = warehouse.ToByteArray();
        return BitConverter.ToInt64(a, 8) ^ BitConverter.ToInt64(b, 8) ^ 0x5374_6f63_6b00_0000; // "Stock"
    }

    public async Task<List<StockLedgerEntry>> RowsForPairAsync(Guid itemId, Guid warehouseId, CancellationToken cancellationToken) =>
        await db.Ledger.Where(e => e.ItemId == itemId && e.WarehouseId == warehouseId && !e.IsCancelled)
            .OrderBy(e => e.PostingDate).ThenBy(e => e.Sequence).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StockEntry>> GetEntriesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.ToList();
        return await db.Entries.Where(e => wanted.Contains(e.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> HasMovementsAsync(Guid itemId, CancellationToken cancellationToken) => db.Ledger.AnyAsync(e => e.ItemId == itemId && !e.IsCancelled, cancellationToken);

    public async Task<IReadOnlyList<StockLedgerEntry>> RowsForVoucherAsync(Guid voucherId, CancellationToken cancellationToken) =>
        await db.Ledger.Where(e => e.VoucherId == voucherId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StockLedgerEntry>> BalancesAsync(Guid companyId, DateOnly asOf, CancellationToken cancellationToken) =>
        await db.Ledger.AsNoTracking()
            .Where(e => e.CompanyId == companyId && !e.IsCancelled && e.PostingDate <= asOf)
            .GroupBy(e => new { e.ItemId, e.WarehouseId })
            .Select(g => g.OrderByDescending(e => e.PostingDate).ThenByDescending(e => e.Sequence).First())
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<StockLedgerEntry>> LedgerAsync(Guid companyId, Guid itemId, Guid? warehouseId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Ledger.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.ItemId == itemId && !e.IsCancelled && e.PostingDate >= from && e.PostingDate <= to
                && (warehouseId == null || e.WarehouseId == warehouseId))
            .OrderBy(e => e.PostingDate).ThenBy(e => e.Sequence).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(IEnumerable<StockLedgerEntry> rows) => db.Ledger.AddRange(rows);

    public Task<StockEntry?> GetEntryAsync(Guid id, CancellationToken cancellationToken) => db.Entries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<StockEntry>> ListEntriesAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Entries.AsNoTracking().Where(e => e.CompanyId == companyId && e.PostingDate >= from && e.PostingDate <= to)
            .OrderBy(e => e.PostingDate).ThenBy(e => e.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(StockEntry entry) => db.Entries.Add(entry);
}

/// <summary>Goods received against purchase orders: submitted stock receipts whose reference is the order number (for Buying).</summary>
internal sealed class StockPurchaseReceipts(StockDbContext db) : Gebeta.Buying.Contracts.IPurchaseReceiptLookup
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Gebeta.Buying.Contracts.OrderedItemQty>>> ReceivedAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken)
    {
        var names = orderNumbers.ToList();
        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Purpose == StockEntryPurpose.Receipt && e.DocStatus == Gebeta.SharedKernel.DocStatus.Submitted
                && e.Reference != null && names.Contains(e.Reference))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entries.GroupBy(e => e.Reference!).ToDictionary(g => g.Key,
            g => (IReadOnlyList<Gebeta.Buying.Contracts.OrderedItemQty>)g.SelectMany(e => e.Lines)
                .GroupBy(l => l.ItemId).Select(x => new Gebeta.Buying.Contracts.OrderedItemQty(x.Key, x.Sum(l => l.Qty))).ToList());
    }
}

/// <summary>Goods delivered against sales orders: submitted stock issues whose reference is the order number (for Selling).</summary>
internal sealed class StockSalesDeliveries(StockDbContext db) : Gebeta.Selling.Contracts.ISalesDeliveryLookup
{
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<Gebeta.Selling.Contracts.OrderedItemQty>>> DeliveredAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken)
    {
        var names = orderNumbers.ToList();
        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.CompanyId == companyId && e.Purpose == StockEntryPurpose.Issue && e.DocStatus == Gebeta.SharedKernel.DocStatus.Submitted
                && e.Reference != null && names.Contains(e.Reference))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return entries.GroupBy(e => e.Reference!).ToDictionary(g => g.Key,
            g => (IReadOnlyList<Gebeta.Selling.Contracts.OrderedItemQty>)g.SelectMany(e => e.Lines)
                .GroupBy(l => l.ItemId).Select(x => new Gebeta.Selling.Contracts.OrderedItemQty(x.Key, x.Sum(l => l.Qty))).ToList());
    }
}

internal sealed class StockNumbers(StockDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), IStockNumbers;

internal sealed class StockMigrator(StockDbContext db) : IModuleMigrator
{
    public string ModuleId => StockModule.Id;

    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}

internal sealed class StockDbContextDesignTimeFactory : IDesignTimeDbContextFactory<StockDbContext>
{
    public StockDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StockDbContext>();
        StockDbContext.Configure(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new StockDbContext(options.Options);
    }
}
