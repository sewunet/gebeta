using Gebeta.Modularity;
using Gebeta.Payroll.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Payroll.Infrastructure.Persistence;

/// <summary>Payroll's database: PostgreSQL schema <c>payroll</c>, with its own migration history.</summary>
public sealed class PayrollDbContext(DbContextOptions<PayrollDbContext> options, TimeProvider? clock = null)
    : ModuleDbContext(options, clock), IPayrollUnitOfWork
{
    public const string SchemaName = "payroll";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<SalaryStructureDefinition> SalaryStructures => Set<SalaryStructureDefinition>();
    public DbSet<SalaryStructureAssignment> Assignments => Set<SalaryStructureAssignment>();
    public DbSet<SalarySlip> SalarySlips => Set<SalarySlip>();
    public DbSet<SalaryBackPayment> BackPayments => Set<SalaryBackPayment>();
    public DbSet<AdditionalPayEntry> AdditionalPay => Set<AdditionalPayEntry>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, SchemaName))
            .UseSnakeCaseNamingConvention();
}

internal sealed class SalaryStructureConfiguration : IEntityTypeConfiguration<SalaryStructureDefinition>
{
    public void Configure(EntityTypeBuilder<SalaryStructureDefinition> builder)
    {
        builder.ToTable("salary_structure");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(140).IsRequired();
        builder.HasIndex(s => new { s.CompanyId, s.Name }).IsUnique();
        builder.Property<List<SalaryComponentLine>>("_components").HasColumnName("components").IsJsonList();
        builder.Ignore(s => s.Components);
        builder.Property(s => s.CreatedBy).HasMaxLength(140);
        builder.Property(s => s.ModifiedBy).HasMaxLength(140);
        builder.Ignore(s => s.DomainEvents);
    }
}

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<SalaryStructureAssignment>
{
    public void Configure(EntityTypeBuilder<SalaryStructureAssignment> builder)
    {
        builder.ToTable("salary_structure_assignment");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Base).HasPrecision(21, 2);
        builder.HasIndex(a => new { a.EmployeeId, a.FromDate }).IsUnique();
        builder.Property(a => a.CreatedBy).HasMaxLength(140);
        builder.Property(a => a.ModifiedBy).HasMaxLength(140);
        builder.Ignore(a => a.DomainEvents);
    }
}

internal sealed class SalarySlipConfiguration : IEntityTypeConfiguration<SalarySlip>
{
    public void Configure(EntityTypeBuilder<SalarySlip> builder)
    {
        builder.ToTable("salary_slip");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(60);
        builder.HasIndex(s => new { s.CompanyId, s.Name }).IsUnique();
        builder.HasIndex(s => new { s.EmployeeId, s.PeriodStart });
        builder.HasIndex(s => new { s.CompanyId, s.PeriodStart });
        builder.Property(s => s.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.EmployeeNumber).HasMaxLength(40).IsRequired();
        builder.Property(s => s.EmployeeName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.StructureName).HasMaxLength(140).IsRequired();
        builder.Property(s => s.TaxTable).HasMaxLength(40).IsRequired();
        foreach (var money in new[] { nameof(SalarySlip.Base), nameof(SalarySlip.GrossPay), nameof(SalarySlip.TaxableEarnings),
                     nameof(SalarySlip.TotalDeductions), nameof(SalarySlip.NetPay), nameof(SalarySlip.EmployerContributions) })
        {
            builder.Property(money).HasPrecision(21, 2);
        }

        builder.Property(s => s.PaymentDays).HasPrecision(9, 3);
        builder.Property(s => s.TotalWorkingDays).HasPrecision(9, 3);
        builder.Property<List<SalarySlipLine>>("_lines").HasColumnName("lines").IsJsonList();
        builder.Ignore(s => s.Lines);
        builder.Ignore(s => s.DocumentType);
        builder.Property(s => s.CreatedBy).HasMaxLength(140);
        builder.Property(s => s.ModifiedBy).HasMaxLength(140);
        builder.Ignore(s => s.DomainEvents);
    }
}

internal sealed class SalaryStructureRepository(PayrollDbContext db) : ISalaryStructureRepository
{
    public Task<SalaryStructureDefinition?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.SalaryStructures.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SalaryStructureDefinition>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.SalaryStructures.AsNoTracking().Where(s => s.CompanyId == companyId).OrderBy(s => s.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> NameExistsAsync(Guid companyId, string name, CancellationToken cancellationToken) =>
        db.SalaryStructures.AnyAsync(s => s.CompanyId == companyId && s.Name == name, cancellationToken);

    public void Add(SalaryStructureDefinition structure) => db.SalaryStructures.Add(structure);
}

internal sealed class AssignmentRepository(PayrollDbContext db) : ISalaryStructureAssignmentRepository
{
    public async Task<IReadOnlyList<SalaryStructureAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.Assignments.AsNoTracking().Where(a => a.EmployeeId == employeeId).OrderBy(a => a.FromDate).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<SalaryStructureAssignment?> InForceOnAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken) =>
        db.Assignments.AsNoTracking().Where(a => a.EmployeeId == employeeId && a.FromDate <= date)
            .OrderByDescending(a => a.FromDate).FirstOrDefaultAsync(cancellationToken);

    public void Add(SalaryStructureAssignment assignment) => db.Assignments.Add(assignment);
}

internal sealed class SalarySlipRepository(PayrollDbContext db) : ISalarySlipRepository
{
    public Task<SalarySlip?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.SalarySlips.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SalarySlip>> ListAsync(Guid companyId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken) =>
        await db.SalarySlips.AsNoTracking()
            .Where(s => s.CompanyId == companyId && (from == null || s.PeriodEnd >= from) && (to == null || s.PeriodStart <= to))
            .OrderBy(s => s.PeriodStart).ThenBy(s => s.EmployeeNumber)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> HasActiveSlipAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken) =>
        db.SalarySlips.AnyAsync(s => s.EmployeeId == employeeId && s.DocStatus != Gebeta.SharedKernel.DocStatus.Cancelled
            && s.PeriodStart <= periodEnd && s.PeriodEnd >= periodStart, cancellationToken);

    public async Task<IReadOnlyList<SalarySlip>> SubmittedForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.SalarySlips.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId && s.DocStatus == Gebeta.SharedKernel.DocStatus.Submitted && s.PeriodStart >= from && s.PeriodStart <= to)
            .OrderBy(s => s.PeriodStart).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(SalarySlip slip) => db.SalarySlips.Add(slip);
}

internal sealed class BackPaymentConfiguration : IEntityTypeConfiguration<SalaryBackPayment>
{
    public void Configure(EntityTypeBuilder<SalaryBackPayment> builder)
    {
        builder.ToTable("salary_back_payment");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Name).HasMaxLength(60);
        builder.HasIndex(b => new { b.CompanyId, b.Name }).IsUnique();
        builder.HasIndex(b => new { b.EmployeeId, b.EffectiveFrom });
        builder.Property(b => b.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.EmployeeNumber).HasMaxLength(40).IsRequired();
        builder.Property(b => b.EmployeeName).HasMaxLength(200).IsRequired();
        builder.Property(b => b.NewBase).HasPrecision(21, 2);
        builder.Property(b => b.Reason).HasMaxLength(500).IsRequired();
        builder.Property(b => b.Reference).HasMaxLength(140);
        builder.Property<List<BackPaymentMonth>>("_months").HasColumnName("months").IsJsonList();
        builder.Ignore(b => b.Months);
        builder.HasMany(b => b.Entries).WithOne().HasForeignKey(e => e.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(b => b.Entries).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("_entries");
        foreach (var computed in new[] { nameof(SalaryBackPayment.TotalGrossArrears), nameof(SalaryBackPayment.TotalAdditionalTax),
                     nameof(SalaryBackPayment.TotalOtherDeductions), nameof(SalaryBackPayment.TotalEmployerContributions), nameof(SalaryBackPayment.NetArrears) })
        {
            builder.Ignore(computed);
        }

        builder.Ignore(b => b.DocumentType);
        builder.Property(b => b.CreatedBy).HasMaxLength(140);
        builder.Property(b => b.ModifiedBy).HasMaxLength(140);
        builder.Ignore(b => b.DomainEvents);
    }
}

internal sealed class AdditionalPayConfiguration : IEntityTypeConfiguration<AdditionalPayEntry>
{
    public void Configure(EntityTypeBuilder<AdditionalPayEntry> builder)
    {
        builder.ToTable("additional_pay");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Name).HasMaxLength(140).IsRequired();
        builder.Property(e => e.Abbreviation).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Amount).HasPrecision(21, 2);
        builder.Property(e => e.SourceType).HasMaxLength(60).IsRequired();
        builder.Property(e => e.SourceNumber).HasMaxLength(60);
        builder.HasIndex(e => new { e.EmployeeId, e.PayrollDate });
        builder.HasIndex(e => e.SalarySlipId);
        builder.Property(e => e.CreatedBy).HasMaxLength(140);
        builder.Property(e => e.ModifiedBy).HasMaxLength(140);
        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class BackPaymentRepository(PayrollDbContext db) : IBackPaymentRepository
{
    public Task<SalaryBackPayment?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.BackPayments.Include(b => b.Entries).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SalaryBackPayment>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.BackPayments.AsNoTracking().Include(b => b.Entries).Where(b => b.CompanyId == companyId)
            .OrderByDescending(b => b.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<SalaryBackPayment>> SubmittedOverlappingAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.BackPayments.AsNoTracking()
            .Where(b => b.EmployeeId == employeeId && b.DocStatus == Gebeta.SharedKernel.DocStatus.Submitted && b.EffectiveFrom <= to && b.EffectiveTo >= from)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(SalaryBackPayment backPayment) => db.BackPayments.Add(backPayment);
}

internal sealed class AdditionalPayRepository(PayrollDbContext db) : IAdditionalPayRepository
{
    public async Task<IReadOnlyList<AdditionalPayEntry>> PendingAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken) =>
        await db.AdditionalPay.Where(e => e.EmployeeId == employeeId && !e.IsCancelled && e.SalarySlipId == null
                && e.PayrollDate >= periodStart && e.PayrollDate <= periodEnd)
            .OrderBy(e => e.CreatedAt).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AdditionalPayEntry>> ForSlipAsync(Guid salarySlipId, CancellationToken cancellationToken) =>
        await db.AdditionalPay.Where(e => e.SalarySlipId == salarySlipId).ToListAsync(cancellationToken).ConfigureAwait(false);
}

internal sealed class PayrollNumbers(PayrollDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), IPayrollNumbers;

/// <summary>Asks the payment-days capability (HR when installed, otherwise Payroll's fallback).</summary>
internal sealed class PayrollPaymentDays([FromKeyedServices(PayrollModule.Id)] IPaymentDaysProvider provider) : IPayrollPaymentDays
{
    public Task<decimal?> GetAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, decimal totalWorkingDays, CancellationToken cancellationToken) =>
        provider.GetPaymentDaysAsync(employeeId, periodStart, periodEnd, totalWorkingDays, cancellationToken);
}

/// <summary>Fallback without HR: no attendance data, so Payroll uses joining and leaving dates.</summary>
internal sealed class NoAttendanceData : IPaymentDaysProvider
{
    public Task<decimal?> GetPaymentDaysAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, decimal totalWorkingDays, CancellationToken cancellationToken) =>
        Task.FromResult<decimal?>(null);
}

internal sealed class PayrollMigrator(PayrollDbContext db) : IModuleMigrator
{
    public string ModuleId => PayrollModule.Id;

    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without starting the host.</summary>
internal sealed class PayrollDbContextDesignTimeFactory : IDesignTimeDbContextFactory<PayrollDbContext>
{
    public PayrollDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>();
        PayrollDbContext.Configure(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new PayrollDbContext(options.Options);
    }
}
