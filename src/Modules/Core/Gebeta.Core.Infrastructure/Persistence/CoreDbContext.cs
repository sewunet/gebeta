using Gebeta.Core.Application.Companies;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Employees;
using Gebeta.Core.Domain.Security;
using Gebeta.Core.Domain.Settings;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Core.Infrastructure.Persistence;

/// <summary>Core's database: PostgreSQL schema <c>core</c>, with its own migration history.</summary>
public sealed class CoreDbContext(DbContextOptions<CoreDbContext> options, TimeProvider? clock = null)
    : ModuleDbContext(options, clock), ICoreUnitOfWork
{
    public const string SchemaName = "core";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public override string Schema => SchemaName;

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<NamingSeries> NamingSeries => Set<NamingSeries>();
    public DbSet<Holiday> Holidays => Set<Holiday>();
    public DbSet<Employee> Employees => Set<Employee>();
}
