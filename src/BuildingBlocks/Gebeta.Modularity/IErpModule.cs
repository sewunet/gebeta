using Microsoft.AspNetCore.Routing;

namespace Gebeta.Modularity;

/// <summary>Identity of a module. <see cref="Id"/> is lower-case and stable (core, hr, payroll, ...).</summary>
public sealed record ModuleInfo(string Id, string Name, Version Version);

/// <summary>
/// A Gebeta module (docs/plugin-architecture.md). Every business module needs only <c>core</c>;
/// other modules it works better with are listed in <see cref="Enhances"/> and are optional.
/// </summary>
public interface IErpModule
{
    ModuleInfo Info { get; }

    /// <summary>Modules that must be installed. Business modules list only "core".</summary>
    IReadOnlyList<string> Requires { get; }

    /// <summary>Modules that add features to this one when installed too. Never required.</summary>
    IReadOnlyList<string> Enhances { get; }

    /// <summary>Registers services, capabilities and fallbacks.</summary>
    void Register(ModuleRegistrationContext context);

    /// <summary>Maps HTTP endpoints, normally under <c>/api/{Info.Id}</c>.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
