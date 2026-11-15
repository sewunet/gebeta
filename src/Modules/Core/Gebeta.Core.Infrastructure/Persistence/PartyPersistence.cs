using Gebeta.Core.Domain.Parties;
using Gebeta.SharedKernel.Ethiopia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class PartyConfiguration : IEntityTypeConfiguration<Party>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.ToTable("party");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Name).HasMaxLength(140).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Tin)
            .HasConversion(t => t.HasValue ? t.Value.Value : null, v => v == null ? null : TaxpayerIdentificationNumber.Create(v).Value)
            .HasMaxLength(TaxpayerIdentificationNumber.Length).IsFixedLength();
        builder.Property(p => p.VatRegistrationNumber).HasMaxLength(30);
        builder.Property(p => p.Phone).HasMaxLength(30);
        builder.HasIndex(p => new { p.CompanyId, p.Name });
        builder.Ignore(p => p.IsVatRegistered);
        builder.Property(p => p.CreatedBy).HasMaxLength(140);
        builder.Property(p => p.ModifiedBy).HasMaxLength(140);
        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class PartyRepository(CoreDbContext db) : IPartyRepository
{
    public Task<Party?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Set<Party>().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Party>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Set<Party>().AsNoTracking().Where(p => p.CompanyId == companyId).OrderBy(p => p.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Party party) => db.Set<Party>().Add(party);
}
