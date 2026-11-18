using Gebeta.Core.Domain.CostCenters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> builder)
    {
        builder.ToTable("cost_center");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
        builder.HasIndex(c => c.CompanyId).IsUnique().HasFilter("is_main"); // one Main per company
        builder.Property(c => c.Name).HasMaxLength(140).IsRequired();
        builder.Property(c => c.CreatedBy).HasMaxLength(140);
        builder.Property(c => c.ModifiedBy).HasMaxLength(140);
        builder.Ignore(c => c.DomainEvents);
    }
}

internal sealed class CostCenterRepository(CoreDbContext db) : ICostCenterRepository
{
    public Task<CostCenter?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Set<CostCenter>().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CostCenter>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Set<CostCenter>().AsNoTracking().Where(c => c.CompanyId == companyId).OrderByDescending(c => c.IsMain).ThenBy(c => c.Code)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken) =>
        db.Set<CostCenter>().AnyAsync(c => c.CompanyId == companyId && c.Code == code, cancellationToken);

    public void Add(CostCenter costCenter) => db.Set<CostCenter>().Add(costCenter);
}
