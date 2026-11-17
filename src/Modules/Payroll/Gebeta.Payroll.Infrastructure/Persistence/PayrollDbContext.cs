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
    public DbSet<PayrollRun> Runs => Set<PayrollRun>();
    public DbSet<EmployeePayrollProfile> Profiles => Set<EmployeePayrollProfile>();

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
        builder.Property<List<string>>("_variables").HasColumnName("variables").IsJsonList();
        builder.Ignore(s => s.Variables);
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
        builder.Property<List<PayValue>>("_values").HasColumnName("pay_values").IsJsonList();
        builder.Ignore(a => a.Values);
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
        builder.Property<List<AdditionalPay>>("_additional").HasColumnName("additional").IsJsonList();
        builder.Ignore(s => s.Additional);
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

    public Task<bool> NumberExistsAsync(Guid companyId, string number, CancellationToken cancellationToken) =>
        db.SalarySlips.AnyAsync(s => s.CompanyId == companyId && s.Name == number, cancellationToken);

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

internal sealed class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("payroll_run");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(60);
        builder.HasIndex(r => new { r.CompanyId, r.Name }).IsUnique();
        builder.HasIndex(r => new { r.CompanyId, r.PeriodStart });
        builder.Property(r => r.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property<List<Guid>>("_slipIds").HasColumnName("slip_ids").IsRequired();
        builder.Ignore(r => r.SlipIds);
        builder.Property<List<PayrollRunIssue>>("_issues").HasColumnName("issues").IsJsonList();
        builder.Ignore(r => r.Issues);
        builder.Ignore(r => r.DocumentType);
        builder.Property(r => r.CreatedBy).HasMaxLength(140);
        builder.Property(r => r.ModifiedBy).HasMaxLength(140);
        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class PayrollProfileConfiguration : IEntityTypeConfiguration<EmployeePayrollProfile>
{
    public void Configure(EntityTypeBuilder<EmployeePayrollProfile> builder)
    {
        builder.ToTable("employee_payroll_profile");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.HasIndex(p => p.EmployeeId).IsUnique();
        builder.HasIndex(p => p.CompanyId);
        builder.Property(p => p.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.BankName).HasMaxLength(100);
        builder.Property(p => p.BankAccountNumber).HasMaxLength(30);
        builder.Property(p => p.PensionNumber).HasMaxLength(40);
        builder.Property(p => p.CreatedBy).HasMaxLength(140);
        builder.Property(p => p.ModifiedBy).HasMaxLength(140);
        builder.Ignore(p => p.DomainEvents);
    }
}

internal sealed class PayrollRunRepository(PayrollDbContext db) : IPayrollRunRepository
{
    public Task<PayrollRun?> GetAsync(Guid id, CancellationToken cancellationToken) => db.Runs.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PayrollRun>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Runs.AsNoTracking().Where(r => r.CompanyId == companyId).OrderByDescending(r => r.PeriodStart).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(PayrollRun run) => db.Runs.Add(run);
}

internal sealed class PayrollProfileRepository(PayrollDbContext db) : IPayrollProfileRepository
{
    public Task<EmployeePayrollProfile?> GetAsync(Guid employeeId, CancellationToken cancellationToken) =>
        db.Profiles.FirstOrDefaultAsync(p => p.EmployeeId == employeeId, cancellationToken);

    public async Task<IReadOnlyList<EmployeePayrollProfile>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        await db.Profiles.AsNoTracking().Where(p => p.CompanyId == companyId).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(EmployeePayrollProfile profile) => db.Profiles.Add(profile);
}
