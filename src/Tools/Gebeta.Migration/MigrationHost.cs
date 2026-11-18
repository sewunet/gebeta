using Gebeta.Application;
using Gebeta.Composition;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gebeta.Migration;

/// <summary>The user every migrated record is created by; a bootstrap System Manager, so it can see and create everything.</summary>
internal sealed class MigrationUser : ICurrentUser
{
    public const string Name = "gebeta.migration";

    public bool IsAuthenticated => true;
    public string? ExternalId => "gebeta-migration";
    public string? Username => Name;
    public string? Email => null;
    public string? FullName => "Data migration";
}

/// <summary>
/// Gebeta's modules, loaded in this process exactly as the API loads them (same catalogue), so the migration writes through
/// the application layer and every rule runs. Integration events go to each module's outbox; the running API server's
/// dispatchers deliver them (Accounting sets up a migrated company's chart, for example).
/// </summary>
public static class MigrationHost
{
    /// <param name="modules">The modules this installation has (Core is always added); all of them when null.</param>
    public static ServiceProvider Build(string connectionString, Action<IConfigurationBuilder>? configure = null, IReadOnlyCollection<string>? modules = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Main"] = connectionString,
            ["Security:BootstrapAdmins:0"] = MigrationUser.Name,
            ["RabbitMq:Enabled"] = "false",
        };
        var enabled = Installed(modules);
        for (var i = 0; i < enabled.Count; i++)
        {
            settings[$"Modules:Enabled:{i}"] = enabled[i];
        }

        var builder = new ConfigurationBuilder().AddInMemoryCollection(settings);
        configure?.Invoke(builder);
        var configuration = builder.Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(l => l.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning));
        services.AddGebetaMessaging(configuration);
        ModuleLoader.Load(services, configuration, ModuleCatalogue.All());
        services.AddScoped<ICurrentUser, MigrationUser>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>The installation's modules: those given (Core always), or every module in the catalogue.</summary>
    public static IReadOnlyList<string> Installed(IReadOnlyCollection<string>? modules) =>
        modules is { Count: > 0 }
            ? ["core", .. modules.Select(m => m.Trim().ToLowerInvariant()).Where(m => m.Length > 0 && m != "core").Distinct()]
            : ModuleCatalogue.All().Select(m => m.Info.Id).ToList();
}
