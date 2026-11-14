using System.Reflection;

namespace Gebeta.ArchitectureTests;

/// <summary>
/// Clean architecture and plugin rules (ADR 0006, ADR 0008), checked on the compiled assemblies:
///
///   Presentation ─► Application ─► Domain ─► SharedKernel
///        ▲               │
///   Infrastructure ──────┘   (composition root of the module)
///
/// and a module may use another module only through its .Contracts assembly.
/// </summary>
public class LayerRulesTests
{
    private static readonly string[] BuildingBlocks = ["Gebeta.SharedKernel", "Gebeta.Application", "Gebeta.Modularity"];

    private static readonly IReadOnlyList<Assembly> Assemblies = Directory
        .GetFiles(AppContext.BaseDirectory, "Gebeta.*.dll")
        .Select(Path.GetFileNameWithoutExtension)
        .Where(n => n is not null && !n.EndsWith("Tests", StringComparison.Ordinal))
        .Select(n => Assembly.Load(n!))
        .ToList();

    private static IEnumerable<Assembly> ModuleAssemblies(string layer) =>
        Assemblies.Where(a => ModuleOf(a) is not null && a.GetName().Name!.EndsWith("." + layer, StringComparison.Ordinal));

    private static List<string> GebetaReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(r => r.Name!).Where(n => n.StartsWith("Gebeta.", StringComparison.Ordinal)).ToList();

    /// <summary>"Gebeta.Payroll.Domain" → "Payroll"; building blocks and the host → null.</summary>
    private static string? ModuleOf(Assembly assembly) => ModuleOf(assembly.GetName().Name!);

    private static string? ModuleOf(string name)
    {
        if (BuildingBlocks.Contains(name) || name == "Gebeta.Api")
        {
            return null;
        }

        var parts = name.Split('.');
        return parts.Length >= 3 ? parts[1] : null;
    }

    [Fact]
    public void The_assemblies_are_found()
    {
        Assert.Contains(Assemblies, a => a.GetName().Name == "Gebeta.Core.Domain");
        Assert.Contains(Assemblies, a => a.GetName().Name == "Gebeta.SharedKernel");
    }

    [Fact]
    public void SharedKernel_depends_on_nothing_in_Gebeta()
    {
        var kernel = Assemblies.Single(a => a.GetName().Name == "Gebeta.SharedKernel");

        Assert.Empty(GebetaReferences(kernel));
    }

    [Fact]
    public void Domain_depends_only_on_the_shared_kernel()
    {
        foreach (var domain in ModuleAssemblies("Domain"))
        {
            var wrong = GebetaReferences(domain).Where(r => r != "Gebeta.SharedKernel").ToList();
            Assert.True(wrong.Count == 0, $"{domain.GetName().Name} must not reference {string.Join(", ", wrong)}");
        }
    }

    [Fact]
    public void Domain_and_Application_do_not_depend_on_frameworks()
    {
        string[] forbidden = ["Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore", "Npgsql", "RabbitMQ", "Wolverine"];
        foreach (var assembly in ModuleAssemblies("Domain").Concat(ModuleAssemblies("Application")))
        {
            var wrong = assembly.GetReferencedAssemblies().Select(r => r.Name!)
                .Where(n => forbidden.Any(f => n.StartsWith(f, StringComparison.Ordinal))).ToList();
            Assert.True(wrong.Count == 0, $"{assembly.GetName().Name} must not reference {string.Join(", ", wrong)}");
        }
    }

    [Fact]
    public void Application_does_not_depend_on_Infrastructure_or_Presentation()
    {
        foreach (var application in ModuleAssemblies("Application"))
        {
            var wrong = GebetaReferences(application)
                .Where(r => r.EndsWith(".Infrastructure", StringComparison.Ordinal) || r.EndsWith(".Presentation", StringComparison.Ordinal))
                .ToList();
            Assert.True(wrong.Count == 0, $"{application.GetName().Name} must not reference {string.Join(", ", wrong)}");
        }
    }

    [Fact]
    public void Presentation_does_not_depend_on_Infrastructure()
    {
        foreach (var presentation in ModuleAssemblies("Presentation"))
        {
            var wrong = GebetaReferences(presentation).Where(r => r.EndsWith(".Infrastructure", StringComparison.Ordinal)).ToList();
            Assert.True(wrong.Count == 0, $"{presentation.GetName().Name} must not reference {string.Join(", ", wrong)}");
        }
    }

    [Fact]
    public void Contracts_depend_on_nothing_in_Gebeta()
    {
        foreach (var contracts in ModuleAssemblies("Contracts"))
        {
            Assert.True(GebetaReferences(contracts).Count == 0,
                $"{contracts.GetName().Name} must stay free of Gebeta dependencies so any module can use it");
        }
    }

    [Fact]
    public void Modules_use_other_modules_only_through_their_contracts()
    {
        foreach (var assembly in Assemblies.Where(a => ModuleOf(a) is not null))
        {
            var module = ModuleOf(assembly);
            var wrong = GebetaReferences(assembly)
                .Where(r => ModuleOf(r) is { } other && other != module && !r.EndsWith(".Contracts", StringComparison.Ordinal))
                .ToList();
            Assert.True(wrong.Count == 0, $"{assembly.GetName().Name} may use other modules only via .Contracts, not {string.Join(", ", wrong)}");
        }
    }
}
