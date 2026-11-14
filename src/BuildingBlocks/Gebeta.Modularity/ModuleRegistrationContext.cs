using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Modularity;

/// <summary>What a module gets while it registers itself.</summary>
public sealed class ModuleRegistrationContext
{
    internal ModuleRegistrationContext(IErpModule module, IServiceCollection services, IConfiguration configuration,
        CapabilityRegistrations capabilities, IReadOnlySet<string> installedModules)
    {
        Module = module;
        Services = services;
        Configuration = configuration;
        Capabilities = new ModuleCapabilities(module.Info.Id, capabilities);
        InstalledModules = installedModules;
    }

    public IErpModule Module { get; }
    public IServiceCollection Services { get; }
    public IConfiguration Configuration { get; }
    public ModuleCapabilities Capabilities { get; }

    /// <summary>Ids of all modules running in this host.</summary>
    public IReadOnlySet<string> InstalledModules { get; }

    public bool IsInstalled(string moduleId) => InstalledModules.Contains(moduleId);
}

/// <summary>
/// Declares the capabilities a module provides to others, and the fallbacks it uses
/// when no installed module provides a capability it needs.
/// </summary>
public sealed class ModuleCapabilities
{
    private readonly string _moduleId;
    private readonly CapabilityRegistrations _registrations;

    internal ModuleCapabilities(string moduleId, CapabilityRegistrations registrations)
    {
        _moduleId = moduleId;
        _registrations = registrations;
    }

    /// <summary>This module provides <typeparamref name="TCapability"/> to every module.</summary>
    public ModuleCapabilities Provide<TCapability, TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TCapability : class
        where TImplementation : class, TCapability
    {
        _registrations.AddProvider(typeof(TCapability), typeof(TImplementation), _moduleId, lifetime);
        return this;
    }

    /// <summary>
    /// This module needs <typeparamref name="TCapability"/>; if no installed module provides it,
    /// this module uses <typeparamref name="TFallback"/>. Resolve it with
    /// <c>[FromKeyedServices("&lt;module id&gt;")] TCapability</c>.
    /// </summary>
    public ModuleCapabilities UseWithFallback<TCapability, TFallback>(ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TCapability : class
        where TFallback : class, TCapability
    {
        _registrations.AddFallback(typeof(TCapability), typeof(TFallback), _moduleId, lifetime);
        return this;
    }
}
