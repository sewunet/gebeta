using Gebeta.Core.Domain.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class NamingSeriesConfiguration : IEntityTypeConfiguration<NamingSeries>
{
    public void Configure(EntityTypeBuilder<NamingSeries> builder)
    {
        builder.ToTable("naming_series");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.DocumentType).HasMaxLength(80).IsRequired();
        builder.Property(s => s.Pattern).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => new { s.CompanyId, s.DocumentType }).IsUnique();
        builder.Property(s => s.CreatedBy).HasMaxLength(140);
        builder.Property(s => s.ModifiedBy).HasMaxLength(140);
        builder.Ignore(s => s.DomainEvents);
    }
}

internal sealed class HolidayConfiguration : IEntityTypeConfiguration<Holiday>
{
    public void Configure(EntityTypeBuilder<Holiday> builder)
    {
        builder.ToTable("holiday");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.Name).HasMaxLength(120).IsRequired();
        builder.Property(h => h.AmharicName).HasMaxLength(120);
        builder.HasIndex(h => h.Date);
        builder.Property(h => h.CreatedBy).HasMaxLength(140);
        builder.Property(h => h.ModifiedBy).HasMaxLength(140);
        builder.Ignore(h => h.DomainEvents);
    }
}

internal sealed class NamingSeriesRepository(CoreDbContext db) : INamingSeriesRepository
{
    public Task<NamingSeries?> FindAsync(Guid companyId, string documentType, CancellationToken cancellationToken) =>
        db.NamingSeries.FirstOrDefaultAsync(s => s.CompanyId == companyId && s.DocumentType == documentType, cancellationToken);

    public async Task<IReadOnlyList<NamingSeries>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.NamingSeries.AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.DocumentType)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(NamingSeries series) => db.NamingSeries.Add(series);
}

internal sealed class HolidayRepository(CoreDbContext db) : IHolidayRepository
{
    public async Task<IReadOnlyList<Holiday>> ListAsync(DateOnly from, DateOnly to, Guid? companyId, CancellationToken cancellationToken) =>
        await db.Holidays.AsNoTracking()
            .Where(h => h.Date >= from && h.Date <= to && (h.CompanyId == null || h.CompanyId == companyId))
            .OrderBy(h => h.Date)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> ExistsAsync(DateOnly date, string name, Guid? companyId, CancellationToken cancellationToken) =>
        db.Holidays.AnyAsync(h => h.Date == date && h.Name == name && h.CompanyId == companyId, cancellationToken);

    public void Add(Holiday holiday) => db.Holidays.Add(holiday);
}
