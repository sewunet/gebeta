using Gebeta.Selling.Application;
using Gebeta.Selling.Contracts;
using Gebeta.Selling.Domain.Orders;
using Gebeta.Modularity;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Selling.Infrastructure.Persistence;

/// <summary>Selling's database: PostgreSQL schema <c>selling</c>, with its own migration history.</summary>
public sealed class SellingDbContext(DbContextOptions<SellingDbContext> options, TimeProvider? clock = null) : ModuleDbContext(options, clock), ISellingUnitOfWork
{
    public const string SchemaName = "selling";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, SchemaName))
            .UseSnakeCaseNamingConvention();
}

internal sealed class SalesOrderConfiguration : IEntityTypeConfiguration<SalesOrder>
{
    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.ToTable("sales_order");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Name).HasMaxLength(60);
        builder.HasIndex(o => new { o.CompanyId, o.Name }).IsUnique();
        builder.HasIndex(o => new { o.CompanyId, o.PostingDate });
        builder.HasIndex(o => o.CustomerId);
        builder.Property(o => o.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.CustomerName).HasMaxLength(140).IsRequired();
        builder.Property(o => o.Remark).HasMaxLength(300);
        builder.Property<List<SalesOrderLine>>("_lines").HasColumnName("lines").IsJsonList();
        builder.Ignore(o => o.Lines);
        builder.Ignore(o => o.NetTotal);
        builder.Ignore(o => o.DocumentType);
        builder.Property(o => o.CreatedBy).HasMaxLength(140);
        builder.Property(o => o.ModifiedBy).HasMaxLength(140);
        builder.Ignore(o => o.DomainEvents);
    }
}

internal sealed class SalesOrderRepository(SellingDbContext db) : ISalesOrderRepository
{
    public Task<SalesOrder?> GetAsync(Guid id, CancellationToken cancellationToken) => db.SalesOrders.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SalesOrder>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.SalesOrders.AsNoTracking().Where(o => o.CompanyId == companyId && o.PostingDate >= from && o.PostingDate <= to)
            .OrderByDescending(o => o.PostingDate).ThenByDescending(o => o.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(SalesOrder order) => db.SalesOrders.Add(order);
}

internal sealed class SellingNumbers(SellingDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), ISellingNumbers;

/// <summary>Fulfilment through the capabilities other modules provide (or the fallbacks below).</summary>
internal sealed class OrderFulfilment(
    [FromKeyedServices(SellingModule.Id)] ISalesDeliveryLookup deliveries,
    [FromKeyedServices(SellingModule.Id)] ISalesBillingLookup billing) : IOrderFulfilment
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> DeliveredAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken) =>
        deliveries.DeliveredAsync(companyId, orders, cancellationToken);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken) =>
        billing.BilledAsync(companyId, orders, cancellationToken);
}

/// <summary>Without Stock: no deliveries are known.</summary>
internal sealed class NoDeliveries : ISalesDeliveryLookup
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> DeliveredAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>>(new Dictionary<string, IReadOnlyList<OrderedItemQty>>());
}

/// <summary>Without Accounting: no invoices are known.</summary>
internal sealed class NoBilling : ISalesBillingLookup
{
    public Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>>(new Dictionary<string, IReadOnlyList<OrderedItemQty>>());
}

internal sealed class SellingMigrator(SellingDbContext db) : IModuleMigrator
{
    public string ModuleId => SellingModule.Id;

    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}

internal sealed class SellingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SellingDbContext>
{
    public SellingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SellingDbContext>();
        SellingDbContext.Configure(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new SellingDbContext(options.Options);
    }
}
