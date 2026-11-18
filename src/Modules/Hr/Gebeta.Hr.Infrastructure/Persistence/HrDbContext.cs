using Gebeta.Hr.Application;
using Gebeta.Hr.Domain.Leave;
using Gebeta.Modularity;
using Gebeta.Persistence;
using Gebeta.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Hr.Infrastructure.Persistence;

/// <summary>HR's database: PostgreSQL schema <c>hr</c>, with its own migration history.</summary>
public sealed class HrDbContext(DbContextOptions<HrDbContext> options, TimeProvider? clock = null) : ModuleDbContext(options, clock), IHrUnitOfWork
{
    public const string SchemaName = "hr";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();
    public DbSet<LeaveLedgerEntry> LeaveLedger => Set<LeaveLedgerEntry>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, SchemaName))
            .UseSnakeCaseNamingConvention();
}

internal sealed class LeaveApplicationConfiguration : IEntityTypeConfiguration<LeaveApplication>
{
    public void Configure(EntityTypeBuilder<LeaveApplication> builder)
    {
        builder.ToTable("leave_application");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Name).HasMaxLength(60);
        builder.HasIndex(a => new { a.CompanyId, a.Name }).IsUnique();
        builder.HasIndex(a => new { a.EmployeeId, a.FromDate });
        builder.Property(a => a.DocStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.EmployeeName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.Days).HasPrecision(9, 2);
        builder.Property(a => a.Reason).HasMaxLength(500);
        builder.Property(a => a.ApprovedBy).HasMaxLength(140);
        builder.Ignore(a => a.DocumentType);
        builder.Property(a => a.CreatedBy).HasMaxLength(140);
        builder.Property(a => a.ModifiedBy).HasMaxLength(140);
        builder.Ignore(a => a.DomainEvents);
    }
}

internal sealed class LeaveLedgerConfiguration : IEntityTypeConfiguration<LeaveLedgerEntry>
{
    public void Configure(EntityTypeBuilder<LeaveLedgerEntry> builder)
    {
        builder.ToTable("leave_ledger_entry");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Days).HasPrecision(9, 2);
        builder.HasIndex(e => new { e.EmployeeId, e.Type });
        builder.HasIndex(e => new { e.EmployeeId, e.Type, e.FiscalYear }).IsUnique().HasFilter("source = 'Allocation'");
        builder.Property(e => e.CreatedBy).HasMaxLength(140);
        builder.Property(e => e.ModifiedBy).HasMaxLength(140);
        builder.Ignore(e => e.DomainEvents);
    }
}

internal sealed class AttendanceConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("attendance_record");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(r => new { r.EmployeeId, r.Date }).IsUnique();
        builder.HasIndex(r => new { r.CompanyId, r.Date });
        builder.Ignore(r => r.UnpaidDays);
        builder.Property(r => r.CreatedBy).HasMaxLength(140);
        builder.Property(r => r.ModifiedBy).HasMaxLength(140);
        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class LeaveRepository(HrDbContext db) : ILeaveRepository
{
    public Task<LeaveApplication?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.LeaveApplications.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<LeaveApplication>> ForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.LeaveApplications.AsNoTracking().Where(a => a.EmployeeId == employeeId).OrderByDescending(a => a.FromDate).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<LeaveApplication>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.LeaveApplications.AsNoTracking().Where(a => a.CompanyId == companyId && a.FromDate <= to && a.ToDate >= from)
            .OrderBy(a => a.FromDate).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<LeaveApplication>> ApprovedAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.LeaveApplications.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.DocStatus == DocStatus.Submitted && a.FromDate <= to && a.ToDate >= from)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> HasOverlapAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        db.LeaveApplications.AnyAsync(a => a.EmployeeId == employeeId && a.DocStatus != DocStatus.Cancelled && a.FromDate <= to && a.ToDate >= from, cancellationToken);

    public async Task<decimal> BalanceAsync(Guid employeeId, LeaveType type, CancellationToken cancellationToken) =>
        await db.LeaveLedger.Where(e => e.EmployeeId == employeeId && e.Type == type).SumAsync(e => e.Days, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<LeaveLedgerEntry>> LedgerAsync(Guid employeeId, CancellationToken cancellationToken) =>
        await db.LeaveLedger.AsNoTracking().Where(e => e.EmployeeId == employeeId).OrderBy(e => e.Date).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<bool> AllocatedAsync(Guid employeeId, LeaveType type, int fiscalYear, CancellationToken cancellationToken) =>
        db.LeaveLedger.AnyAsync(e => e.EmployeeId == employeeId && e.Type == type && e.FiscalYear == fiscalYear
            && (e.Source == LeaveLedgerSource.Allocation || e.Source == LeaveLedgerSource.Opening), cancellationToken); // an opening balance may include the year

    public void Add(LeaveApplication application) => db.LeaveApplications.Add(application);

    public void Add(LeaveLedgerEntry entry) => db.LeaveLedger.Add(entry);
}

internal sealed class AttendanceRepository(HrDbContext db) : IAttendanceRepository
{
    public Task<AttendanceRecord?> GetAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken) =>
        db.Attendance.FirstOrDefaultAsync(r => r.EmployeeId == employeeId && r.Date == date, cancellationToken);

    public async Task<IReadOnlyList<AttendanceRecord>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Attendance.AsNoTracking().Where(r => r.CompanyId == companyId && r.Date >= from && r.Date <= to).OrderBy(r => r.Date)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AttendanceRecord>> ForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await db.Attendance.AsNoTracking().Where(r => r.EmployeeId == employeeId && r.Date >= from && r.Date <= to)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(AttendanceRecord record) => db.Attendance.Add(record);
}

internal sealed class HrNumbers(HrDbContext db, DocumentNumbering numbering) : ModuleDocumentNumbers(db, numbering), IHrNumbers;

internal sealed class HrMigrator(HrDbContext db) : IModuleMigrator
{
    public string ModuleId => HrModule.Id;

    public Task MigrateAsync(CancellationToken cancellationToken) => db.Database.MigrateAsync(cancellationToken);
}

internal sealed class HrDbContextDesignTimeFactory : IDesignTimeDbContextFactory<HrDbContext>
{
    public HrDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HrDbContext>();
        HrDbContext.Configure(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new HrDbContext(options.Options);
    }
}
