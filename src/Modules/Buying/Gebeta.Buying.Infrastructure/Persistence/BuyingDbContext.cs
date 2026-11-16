using Gebeta.Buying.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Buying.Domain.Orders;
using Gebeta.Modularity;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Buying.Infrastructure.Persistence;

/// <summary>Buying's database: PostgreSQL schema <c>buying</c>, with its own migration history.</summary>
public sealed class BuyingDbContext(DbContextOptions<BuyingDbContext> options, TimeProvider? clock = null) : ModuleDbContext(options, clock), IBuyingUnitOfWork
{
    public const string SchemaName = "buying";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, SchemaName))
            .UseSnakeCaseNamingConvention();
}

internal sealed class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> builder)
    {
        builder.ToTable("purchase_order");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(60);
        builder.HasIndex(o => new { o.CompanyId, o.Name }).IsUnique();
        builder.HasIndex(o => new { o.CompanyId, o.PostingDate });
        builder.HasIndex(o => o.SupplierId);
        builder.Property(o => o.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.SupplierName).HasMaxLength(140).IsRequired();
        builder.Property(o => o.Remark).HasMaxLength(300);
        builder.Property<List<PurchaseOrderLine>>("_lines").HasColumnName("lines").IsJsonList();
        builder.Ignore(o => o.Lines);
        builder.Ignore(o => o.NetTotal);
        builder.Ignore(o => o.DocumentType);
        builder.Property(o => o.CreatedBy).HasMaxLength(140);
        builder.Property(o => o.ModifiedBy).HasMaxLength(140);
        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class PurchaseOrderRepository(BuyingDbContext db) : IPurchaseOrderRepository
{
    public Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken cancellationToken) => db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PurchaseOrder>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.PurchaseOrders.AsNoTracking().Where(o => o.CompanyId == companyId && o.PostingDate >= from && o.PostingDate <= to)
            .OrderByDescending(o => o.PostingDate).ThenByDescending(o => o.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(PurchaseOrder order) => db.PurchaseOrders.Add(order);
}

internal sealed class BuyingNumbers(BuyingDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), IBuyingNumbers;

/// <summary>Fulfilment through the capabilities other modules provide (or the fallbacks below).</summary>
internal sealed class OrderFulfilment(
    [FromKeyedServices(BuyingModule.Id)] IPurchaseReceiptLookup receipts,
    [FromKeyedServices(BuyingModule.Id)] IPurchaseBillingLookup billing) : IOrderFulfilment
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> ReceivedAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken) =>
        receipts.ReceivedAsync(companyId, orders, cancellationToken);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken) =>
        billing.BilledAsync(companyId, orders, cancellationToken);
}

/// <summary>Without Stock: no goods received are known.</summary>
internal sealed class NoReceipts : IPurchaseReceiptLookup
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> ReceivedAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>>(new Dictionary<string, IReadOnlyList<OrderedItemQty>>());
}

/// <summary>Without Accounting: no bills are known.</summary>
internal sealed class NoBilling : IPurchaseBillingLookup
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>>(new Dictionary<string, IReadOnlyList<OrderedItemQty>>());
}

internal sealed class BuyingMigrator(BuyingDbContext db) : IModuleMigrator
{
    public string ModuleId => BuyingModule.Id;

    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}

internal sealed class BuyingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<BuyingDbContext>
{
    public BuyingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BuyingDbContext>();
        BuyingDbContext.Configure(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new BuyingDbContext(options.Options);
    }
}
