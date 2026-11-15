using Gebeta.Core.Application.Employees;
using Gebeta.Core.Domain.Employees;
using Gebeta.Persistence;
using Gebeta.SharedKernel.Ethiopia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employee");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Number).HasMaxLength(40).IsRequired();
        builder.HasIndex(e => new { e.CompanyId, e.Number }).IsUnique();
        builder.Property(e => e.FirstName).HasMaxLength(60).IsRequired();
        builder.Property(e => e.FatherName).HasMaxLength(60).IsRequired();
        builder.Property(e => e.GrandfatherName).HasMaxLength(60);
        builder.Property(e => e.Gender).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Tin)
            .HasConversion(t => t.HasValue ? t.Value.Value : null, v => v == null ? null : TaxpayerIdentificationNumber.Create(v).Value)
            .HasMaxLength(TaxpayerIdentificationNumber.Length)
            .IsFixedLength();
        builder.HasIndex(e => new { e.CompanyId, e.Tin }).IsUnique().HasFilter("tin IS NOT NULL");
        builder.Property(e => e.CreatedBy).HasMaxLength(140);
        builder.Property(e => e.ModifiedBy).HasMaxLength(140);
        builder.Ignore(e => e.FullName);
        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class EmployeeRepository(CoreDbContext db) : IEmployeeRepository
{
    public Task<Employee?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Employee>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId).OrderBy(e => e.Number)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> TinExistsAsync(Guid companyId, TaxpayerIdentificationNumber tin, CancellationToken cancellationToken) =>
        db.Employees.AnyAsync(e => e.CompanyId == companyId && e.Tin == tin, cancellationToken);

    public void Add(Employee employee) => db.Employees.Add(employee);
}

internal sealed class CoreNumbers(CoreDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), ICoreNumbers;
