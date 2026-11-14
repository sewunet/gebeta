using Gebeta.Modularity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class CoreMigrator(CoreDbContext db, TimeProvider clock) : IModuleMigrator
{
    public string ModuleId => CoreModule.Id;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        await CoreSeed.EnsureSystemRolesAsync(db, clock, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Readiness: Core's database answers.</summary>
internal sealed class CoreDatabaseHealthCheck(CoreDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("Cannot connect to the Core database.");
        }
#pragma warning disable CA1031 // a health check reports any failure instead of throwing
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("Cannot connect to the Core database.", ex);
        }
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the model without starting the host.</summary>
internal sealed class CoreDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CoreDbContext>
{
    public CoreDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CoreDbContext>();
        CoreModule.ConfigureDbContext(options, "Host=localhost;Port=5434;Database=gebeta;Username=gebeta");
        return new CoreDbContext(options.Options);
    }
}
