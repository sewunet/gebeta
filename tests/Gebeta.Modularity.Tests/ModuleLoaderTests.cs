using Gebeta.Modularity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Modularity.Tests;

/// <summary>
/// The plugin rules from docs/plugin-architecture.md: modules run alone with fallbacks, providers
/// replace fallbacks when installed, and wrong set-ups fail at start-up with a clear message.
/// </summary>
public class ModuleLoaderTests
{
    // A capability Payroll needs (journals) and one HR provides (payment days).
    public interface IJournalSink { string Target { get; } }
    public interface IPaymentDaysProvider { decimal PaymentDays(string employee); }

    private sealed class OutboundJournalSink : IJournalSink { public string Target => "payroll outbound list"; }
    private sealed class CashOutboundJournalSink : IJournalSink { public string Target => "cash outbound list"; }
    private sealed class AccountingJournalSink : IJournalSink { public string Target => "general ledger"; }
    private sealed class ErpNextJournalSink : IJournalSink { public string Target => "erpnext"; }
    private sealed class ManualPaymentDays : IPaymentDaysProvider { public decimal PaymentDays(string employee) => 30m; }
    private sealed class AttendancePaymentDays : IPaymentDaysProvider { public decimal PaymentDays(string employee) => 28m; }

    private sealed class TestModule(string id, string[]? requires = null, Action<ModuleRegistrationContext>? register = null) : IErpModule
    {
        public ModuleInfo Info { get; } = new(id, id, new Version(1, 0));
        public IReadOnlyList<string> Requires { get; } = requires ?? (id == "core" ? [] : ["core"]);
        public IReadOnlyList<string> Enhances { get; } = [];
        public void Register(ModuleRegistrationContext context) => register?.Invoke(context);
        public void MapEndpoints(IEndpointRouteBuilder endpoints) { }
    }

    private static readonly TestModule Core = new("core");

    private static readonly TestModule Payroll = new("payroll", register: c => c.Capabilities
        .UseWithFallback<IJournalSink, OutboundJournalSink>()
        .UseWithFallback<IPaymentDaysProvider, ManualPaymentDays>());

    private static readonly TestModule Cash = new("cash", register: c => c.Capabilities
        .UseWithFallback<IJournalSink, CashOutboundJournalSink>());

    private static readonly TestModule Hr = new("hr", register: c => c.Capabilities
        .Provide<IPaymentDaysProvider, AttendancePaymentDays>());

    private static readonly TestModule Accounting = new("accounting", register: c => c.Capabilities
        .Provide<IJournalSink, AccountingJournalSink>());

    private static readonly TestModule ErpNextConnector = new("erpnext-connector", register: c => c.Capabilities
        .Provide<IJournalSink, ErpNextJournalSink>());

    private static readonly IErpModule[] Catalogue = [Core, Payroll, Cash, Hr, Accounting, ErpNextConnector];

    private static (ModuleRegistry Registry, ServiceProvider Services) Load(params string[] enabled)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(enabled.Select((id, i) => new KeyValuePair<string, string?>($"Modules:Enabled:{i}", id)))
            .Build();
        var services = new ServiceCollection();
        var registry = ModuleLoader.Load(services, config, Catalogue);
        return (registry, services.BuildServiceProvider());
    }

    private static T For<T>(ServiceProvider services, string module) where T : notnull =>
        services.CreateScope().ServiceProvider.GetRequiredKeyedService<T>(module);

    [Fact]
    public void Payroll_alone_uses_its_own_fallbacks()
    {
        var (registry, services) = Load("core", "payroll");

        Assert.Equal("payroll outbound list", For<IJournalSink>(services, "payroll").Target);
        Assert.Equal(30m, For<IPaymentDaysProvider>(services, "payroll").PaymentDays("E-001"));
        Assert.All(registry.Capabilities, b => Assert.True(b.IsFallback));
    }

    [Fact]
    public void Installing_hr_gives_payroll_attendance_based_payment_days()
    {
        var (registry, services) = Load("core", "payroll", "hr");

        Assert.Equal(28m, For<IPaymentDaysProvider>(services, "payroll").PaymentDays("E-001"));
        Assert.Equal("payroll outbound list", For<IJournalSink>(services, "payroll").Target);
        Assert.Contains(registry.Capabilities, b => b.Capability == nameof(IPaymentDaysProvider) && b.ProvidedBy == "hr" && !b.IsFallback);
    }

    [Fact]
    public void Each_module_keeps_its_own_fallback_when_no_provider_is_installed()
    {
        var (_, services) = Load("core", "payroll", "cash");

        Assert.Equal("payroll outbound list", For<IJournalSink>(services, "payroll").Target);
        Assert.Equal("cash outbound list", For<IJournalSink>(services, "cash").Target);
    }

    [Fact]
    public void Installing_accounting_sends_every_modules_journals_to_the_ledger()
    {
        var (registry, services) = Load("core", "payroll", "cash", "accounting");

        Assert.Equal("general ledger", For<IJournalSink>(services, "payroll").Target);
        Assert.Equal("general ledger", For<IJournalSink>(services, "cash").Target);
        var binding = Assert.Single(registry.Capabilities, b => b.Capability == nameof(IJournalSink));
        Assert.Equal(["payroll", "cash"], binding.UsedBy);
    }

    [Fact]
    public void Two_providers_for_one_capability_are_refused()
    {
        var ex = Assert.Throws<ModuleLoadException>(() => Load("core", "payroll", "accounting", "erpnext-connector"));

        Assert.Contains("IJournalSink", ex.Message, StringComparison.Ordinal);
        Assert.Contains("accounting", ex.Message, StringComparison.Ordinal);
        Assert.Contains("erpnext-connector", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Core_must_be_enabled()
    {
        var ex = Assert.Throws<ModuleLoadException>(() => Load("payroll"));

        Assert.Contains("'core'", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_modules_are_reported_with_the_available_ones()
    {
        var ex = Assert.Throws<ModuleLoadException>(() => Load("core", "payrol"));

        Assert.Contains("payrol", ex.Message, StringComparison.Ordinal);
        Assert.Contains("payroll", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Required_modules_register_first_whatever_the_configured_order()
    {
        var (registry, _) = Load("payroll", "hr", "core");

        Assert.Equal("core", registry.Modules[0].Info.Id);
        Assert.True(registry.IsInstalled("HR"));
        Assert.False(registry.IsInstalled("accounting"));
    }

    [Fact]
    public void A_missing_requirement_is_reported()
    {
        var reporting = new TestModule("reporting", requires: ["core", "accounting"]);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Modules:Enabled:0", "core"), new("Modules:Enabled:1", "reporting")])
            .Build();

        var ex = Assert.Throws<ModuleLoadException>(() => ModuleLoader.Load(new ServiceCollection(), config, [Core, reporting]));

        Assert.Contains("requires: accounting", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modules_can_ask_which_partners_are_installed()
    {
        var seen = new List<string>();
        var payroll = new TestModule("payroll", register: c => seen.Add(c.IsInstalled("hr") ? "with hr" : "alone"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Modules:Enabled:0", "core"), new("Modules:Enabled:1", "payroll")])
            .Build();

        ModuleLoader.Load(new ServiceCollection(), config, [Core, payroll, Hr]);

        Assert.Equal(["alone"], seen);
    }
}
