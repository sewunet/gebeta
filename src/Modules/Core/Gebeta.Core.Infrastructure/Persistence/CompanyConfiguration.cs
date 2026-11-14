using Gebeta.Core.Domain.Companies;
using Gebeta.SharedKernel.Ethiopia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable("company");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Name).HasMaxLength(140).IsRequired();
        builder.Property(c => c.Abbreviation).HasMaxLength(10).IsRequired();
        builder.HasIndex(c => c.Abbreviation).IsUnique();

        builder.Property(c => c.Tin)
            .HasConversion(tin => tin.Value, value => TaxpayerIdentificationNumber.Create(value).Value)
            .HasMaxLength(TaxpayerIdentificationNumber.Length)
            .IsFixedLength()
            .IsRequired();
        builder.HasIndex(c => c.Tin).IsUnique();

        builder.Property(c => c.LegalForm).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(c => c.FiscalYearType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.VatRegistrationNumber).HasMaxLength(30);
        builder.Property(c => c.TradeLicenseNumber).HasMaxLength(60);
        builder.Property(c => c.DefaultCurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(c => c.TimeZone).HasMaxLength(60).IsRequired();
        builder.Property(c => c.CreatedBy).HasMaxLength(140);
        builder.Property(c => c.ModifiedBy).HasMaxLength(140);

        builder.Ignore(c => c.DomainEvents);
        builder.Ignore(c => c.IsVatRegistered);
    }
}
