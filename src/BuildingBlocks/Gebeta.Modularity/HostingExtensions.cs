using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gebeta.Modularity;

public static class HostingExtensions
{
    /// <summary>Loads the modules listed in <c>Modules:Enabled</c> from <paramref name="catalogue"/>.</summary>
    public static ModuleRegistry AddGebetaModules(this WebApplicationBuilder builder, params IErpModule[] catalogue)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return ModuleLoader.Load(builder.Services, builder.Configuration, catalogue);
    }

    /// <summary>Maps every installed module's endpoints and logs what runs here.</summary>
    public static WebApplication MapGebetaModules(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var registry = app.Services.GetRequiredService<ModuleRegistry>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Gebeta.Modules");

        foreach (var module in registry.Modules)
        {
            module.MapEndpoints(app);
            LogMessages.ModuleLoaded(logger, module.Info.Id, module.Info.Version);
        }

        foreach (var binding in registry.Capabilities)
        {
            LogMessages.CapabilityBound(logger, binding.Capability, binding.ProvidedBy, binding.IsFallback ? " (fallback)" : string.Empty);
        }

        return app;
    }
}

internal static partial class LogMessages
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Module {ModuleId} {Version} loaded")]
    public static partial void ModuleLoaded(ILogger logger, string moduleId, Version version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Capability {Capability} served by {ModuleId}{Fallback}")]
    public static partial void CapabilityBound(ILogger logger, string capability, string moduleId, string fallback);
}
