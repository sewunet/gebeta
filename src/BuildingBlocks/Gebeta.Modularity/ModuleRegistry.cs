namespace Gebeta.Modularity;

/// <summary>The modules running in this host and who serves each capability.</summary>
public sealed class ModuleRegistry
{
    internal ModuleRegistry(IReadOnlyList<IErpModule> modules, IReadOnlyList<CapabilityBinding> capabilities)
    {
        Modules = modules;
        Capabilities = capabilities;
    }

    /// <summary>Installed modules, in registration order (requirements first).</summary>
    public IReadOnlyList<IErpModule> Modules { get; }

    public IReadOnlyList<CapabilityBinding> Capabilities { get; }

    public bool IsInstalled(string moduleId) =>
        Modules.Any(m => string.Equals(m.Info.Id, moduleId, StringComparison.OrdinalIgnoreCase));
}
