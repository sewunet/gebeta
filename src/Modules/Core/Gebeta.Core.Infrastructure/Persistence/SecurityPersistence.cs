using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("app_user"); // "user" is reserved in PostgreSQL
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.ExternalId).HasMaxLength(100).IsRequired();
        builder.HasIndex(u => u.ExternalId).IsUnique();
        builder.Property(u => u.Username).HasMaxLength(140).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();
        builder.Property(u => u.Email).HasMaxLength(254);
        builder.Property(u => u.FullName).HasMaxLength(140);
        builder.Property(u => u.Language).HasMaxLength(5).IsRequired();
        builder.Property(u => u.Calendar).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(u => u.CreatedBy).HasMaxLength(140);
        builder.Property(u => u.ModifiedBy).HasMaxLength(140);

        // Role assignments as a uuid[] column: small, read with the user on every request.
        builder.Property<List<Guid>>("_roleIds").HasColumnName("role_ids").IsRequired();
        builder.Ignore(u => u.RoleIds);
        builder.Property<List<Guid>>("_companyIds").HasColumnName("company_ids").IsRequired();
        builder.Ignore(u => u.CompanyIds);
        builder.Ignore(u => u.DomainEvents);
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("role");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(80).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.CreatedBy).HasMaxLength(140);
        builder.Property(r => r.ModifiedBy).HasMaxLength(140);

        builder.Property<List<string>>("_permissions").HasColumnName("permissions").IsRequired();
        builder.Ignore(r => r.Permissions);
        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class UserRepository(CoreDbContext db) : IUserRepository
{
    public Task<User?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> GetByExternalIdAsync(string externalId, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().OrderBy(u => u.Username).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class RoleRepository(CoreDbContext db) : IRoleRepository
{
    public Task<Role?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Role?> GetByNameAsync(string name, CancellationToken cancellationToken) =>
        db.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Role>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0
            ? []
            : await db.Roles.AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Role role) => db.Roles.Add(role);
}

/// <summary>Creates Gebeta's system roles if they are missing. Safe to run on every migration.</summary>
internal static class CoreSeed
{
    private static readonly (string Name, string Description, string[] Permissions)[] SystemRoles =
    [
        (Role.SystemManager, "Full access to every module. Give to very few people.", ["*"]),
        ("Core Administrator", "Manages companies, users and roles.", ["core.*"]),
        ("Core Viewer", "Reads companies, users, roles, naming series and holidays.",
            [CorePermissions.CompaniesRead, CorePermissions.UsersRead, CorePermissions.RolesRead, CorePermissions.NamingSeriesRead, CorePermissions.HolidaysRead,
             CorePermissions.EmployeesRead]),
        ("Cashier", "Records cash receipts, payments and transfers; requests petty cash.",
            ["cash.cashiers.read", "cash.transactions.create", "cash.petty-cash.request", CorePermissions.CompaniesRead]),
        ("Cash Manager", "Manages cashiers and settings; approves petty cash.", ["cash.*", CorePermissions.CompaniesRead, CorePermissions.EmployeesRead]),
        ("Accountant", "Keeps the books: chart of accounts, journals and financial reports.",
            ["accounting.*", CorePermissions.CompaniesRead, CorePermissions.EmployeesRead, CorePermissions.PartiesRead, CorePermissions.PartiesManage]),
        ("HR Officer", "Manages leave and attendance.",
            ["hr.*", CorePermissions.CompaniesRead, CorePermissions.EmployeesRead, CorePermissions.HolidaysRead]),
        ("Payroll Officer", "Prepares and submits payroll: salary structures, assignments and salary slips.",
            ["payroll.*", CorePermissions.CompaniesRead, CorePermissions.EmployeesRead, CorePermissions.HolidaysRead]),
    ];

    public static async Task EnsureSystemRolesAsync(CoreDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var names = SystemRoles.Select(r => r.Name).ToList();
        var existing = await db.Roles.Where(r => names.Contains(r.Name)).ToDictionaryAsync(r => r.Name, cancellationToken).ConfigureAwait(false);
        foreach (var (name, description, permissions) in SystemRoles)
        {
            if (!existing.TryGetValue(name, out var role))
            {
                role = Role.Create(name, description, isSystem: true).Value;
                role.CreatedAt = clock.GetUtcNow();
                role.CreatedBy = "system";
                db.Roles.Add(role);
            }

            // New permissions added in later releases reach existing installations too.
            foreach (var permission in permissions)
            {
                role.Grant(permission);
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
