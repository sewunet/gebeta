using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gebeta.Modularity;

/// <summary>
/// A module's database schema migrations. Each module owns its schema (core, payroll, ...)
/// and its own migration history, so modules can be installed and upgraded on their own.
/// </summary>
public interface IModuleMigrator
{
    string ModuleId { get; }

    Task MigrateAsync(CancellationToken cancellationToken);
}

public static class ModuleDatabaseExtensions
{
    public const string MigrateArgument = "--migrate";
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>
    /// Runs every installed module's migrations, in module order (Core first).
    /// Used by the migrate container (<c>--migrate</c>) and, in development, at start-up.
    /// </summary>
    public static async Task MigrateModulesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        var registry = services.GetRequiredService<ModuleRegistry>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Gebeta.Migrations");

        await using var scope = services.CreateAsyncScope();
        var migrators = scope.ServiceProvider.GetServices<IModuleMigrator>().ToList();
        foreach (var module in registry.Modules)
        {
            foreach (var migrator in migrators.Where(m => string.Equals(m.ModuleId, module.Info.Id, StringComparison.OrdinalIgnoreCase)))
            {
                MigrationLog.Migrating(logger, module.Info.Id);
                await migrator.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Handles <c>--migrate</c> (migrate and exit: returns true) and <c>Database:MigrateOnStartup</c>
    /// (migrate, then keep running: returns false).
    /// </summary>
    public static async Task<bool> RunMigrationsIfRequestedAsync(this WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(args);

        if (args.Contains(MigrateArgument, StringComparer.Ordinal))
        {
            await app.Services.MigrateModulesAsync().ConfigureAwait(false);
            return true;
        }

        if (app.Configuration.GetValue<bool>(MigrateOnStartupKey))
        {
            await app.Services.MigrateModulesAsync().ConfigureAwait(false);
        }

        return false;
    }
}

internal static partial class MigrationLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Migrating database schema of module {ModuleId}")]
    public static partial void Migrating(ILogger logger, string moduleId);
}
