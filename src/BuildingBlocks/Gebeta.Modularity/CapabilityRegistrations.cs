using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Modularity;

/// <summary>Collects provider and fallback declarations from all modules, then wires them into DI.</summary>
internal sealed class CapabilityRegistrations
{
    private readonly List<Registration> _providers = [];
    private readonly List<Registration> _fallbacks = [];

    public void AddProvider(Type capability, Type implementation, string moduleId, ServiceLifetime lifetime) =>
        _providers.Add(new Registration(capability, implementation, moduleId, lifetime));

    public void AddFallback(Type capability, Type implementation, string moduleId, ServiceLifetime lifetime)
    {
        if (_fallbacks.Any(f => f.Capability == capability && f.ModuleId == moduleId))
        {
            throw new ModuleLoadException($"Module '{moduleId}' declares more than one fallback for {capability.Name}.");
        }

        _fallbacks.Add(new Registration(capability, implementation, moduleId, lifetime));
    }

    /// <summary>
    /// For every capability: one provider → everybody uses it (also under each asking module's key);
    /// no provider → each asking module gets its own fallback under its key;
    /// two providers → error (for example Accounting and the ERPNext connector together).
    /// </summary>
    public IReadOnlyList<CapabilityBinding> Apply(IServiceCollection services)
    {
        var bindings = new List<CapabilityBinding>();
        var capabilities = _providers.Select(p => p.Capability).Concat(_fallbacks.Select(f => f.Capability)).Distinct();

        foreach (var capability in capabilities.OrderBy(c => c.FullName, StringComparer.Ordinal))
        {
            var providers = _providers.Where(p => p.Capability == capability).ToList();
            var askers = _fallbacks.Where(f => f.Capability == capability).ToList();

            if (providers.Count > 1)
            {
                throw new ModuleLoadException(
                    $"{capability.Name} is provided by more than one module ({string.Join(", ", providers.Select(p => p.ModuleId))}). Install only one of them.");
            }

            if (providers.Count == 1)
            {
                var provider = providers[0];
                services.Add(new ServiceDescriptor(capability, provider.Implementation, provider.Lifetime));
                foreach (var asker in askers)
                {
                    services.Add(new ServiceDescriptor(capability, asker.ModuleId,
                        (sp, _) => sp.GetRequiredService(capability), asker.Lifetime));
                }

                bindings.Add(new CapabilityBinding(capability.Name, provider.ModuleId, IsFallback: false,
                    askers.Select(a => a.ModuleId).ToList()));
                continue;
            }

            foreach (var asker in askers)
            {
                services.Add(new ServiceDescriptor(capability, asker.ModuleId, asker.Implementation, asker.Lifetime));
                bindings.Add(new CapabilityBinding(capability.Name, asker.ModuleId, IsFallback: true, [asker.ModuleId]));
            }
        }

        return bindings;
    }

    private sealed record Registration(Type Capability, Type Implementation, string ModuleId, ServiceLifetime Lifetime);
}

/// <summary>Which module serves a capability, and for whom. Shown by <c>/api/modules</c>.</summary>
public sealed record CapabilityBinding(string Capability, string ProvidedBy, bool IsFallback, IReadOnlyList<string> UsedBy);
