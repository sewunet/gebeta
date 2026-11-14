using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Modularity;

public sealed class ModuleLoadException(string message) : Exception(message);

/// <summary>
/// Picks the enabled modules from the catalogue, checks their requirements, orders them,
/// lets each one register, and wires capabilities.
/// </summary>
public static class ModuleLoader
{
    public const string CoreModuleId = "core";
    public const string EnabledModulesKey = "Modules:Enabled";

    public static ModuleRegistry Load(IServiceCollection services, IConfiguration configuration, IEnumerable<IErpModule> catalogue)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(catalogue);

        var available = new Dictionary<string, IErpModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in catalogue)
        {
            if (!available.TryAdd(module.Info.Id, module))
            {
                throw new ModuleLoadException($"Two modules use the id '{module.Info.Id}'.");
            }
        }

        var enabledIds = configuration.GetSection(EnabledModulesKey).Get<string[]>() ?? [];
        var enabled = Resolve(enabledIds, available);
        var ordered = OrderByRequirements(enabled);
        var installed = ordered.Select(m => m.Info.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var capabilities = new CapabilityRegistrations();
        foreach (var module in ordered)
        {
            module.Register(new ModuleRegistrationContext(module, services, configuration, capabilities, installed));
        }

        var bindings = capabilities.Apply(services);
        var registry = new ModuleRegistry(ordered, bindings);
        services.AddSingleton(registry);
        return registry;
    }

    private static List<IErpModule> Resolve(IEnumerable<string> enabledIds, Dictionary<string, IErpModule> available)
    {
        var ids = enabledIds.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (!ids.Contains(CoreModuleId, StringComparer.OrdinalIgnoreCase))
        {
            throw new ModuleLoadException($"The '{CoreModuleId}' module must be enabled ({EnabledModulesKey}).");
        }

        var unknown = ids.Where(id => !available.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
        {
            throw new ModuleLoadException(
                $"Unknown module(s): {string.Join(", ", unknown)}. Available: {string.Join(", ", available.Keys.Order(StringComparer.Ordinal))}.");
        }

        var modules = ids.Select(id => available[id]).ToList();
        foreach (var module in modules)
        {
            var missing = module.Requires.Where(r => !ids.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
            if (missing.Count > 0)
            {
                throw new ModuleLoadException($"Module '{module.Info.Id}' requires: {string.Join(", ", missing)}.");
            }
        }

        return modules;
    }

    /// <summary>Required modules register first (topological order); otherwise keeps the configured order.</summary>
    private static List<IErpModule> OrderByRequirements(List<IErpModule> modules)
    {
        var byId = modules.ToDictionary(m => m.Info.Id, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<IErpModule>();
        var state = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // false = visiting, true = done

        void Visit(IErpModule module, Stack<string> path)
        {
            if (state.TryGetValue(module.Info.Id, out var done))
            {
                if (!done)
                {
                    throw new ModuleLoadException($"Circular module requirement: {string.Join(" → ", path.Reverse())} → {module.Info.Id}.");
                }

                return;
            }

            state[module.Info.Id] = false;
            path.Push(module.Info.Id);
            foreach (var required in module.Requires)
            {
                Visit(byId[required], path);
            }

            path.Pop();
            state[module.Info.Id] = true;
            ordered.Add(module);
        }

        foreach (var module in modules)
        {
            Visit(module, new Stack<string>());
        }

        return ordered;
    }
}
