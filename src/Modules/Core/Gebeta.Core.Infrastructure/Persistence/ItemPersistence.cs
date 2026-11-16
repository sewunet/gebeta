using Gebeta.Core.Domain.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> builder)
    {
        builder.ToTable("item");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Code).HasMaxLength(40).IsRequired();
        builder.HasIndex(i => new { i.CompanyId, i.Code }).IsUnique();
        builder.Property(i => i.Name).HasMaxLength(140).IsRequired();
        builder.Property(i => i.Uom).HasMaxLength(20).IsRequired();
        builder.Property(i => i.Group).HasMaxLength(60);
        builder.Property(i => i.CreatedBy).HasMaxLength(140);
        builder.Property(i => i.ModifiedBy).HasMaxLength(140);
        builder.Ignore(i => i.DomainEvents);
    }
}

internal sealed class ItemRepository(CoreDbContext db) : IItemRepository
{
    public Task<Item?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Set<Item>().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Item>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Set<Item>().AsNoTracking().Where(i => i.CompanyId == companyId).OrderBy(i => i.Code).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken) =>
        db.Set<Item>().AnyAsync(i => i.CompanyId == companyId && i.Code == code, cancellationToken);

    public void Add(Item item) => db.Set<Item>().Add(item);
}
