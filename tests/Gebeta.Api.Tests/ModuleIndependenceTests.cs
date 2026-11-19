using System.Net;
using System.Net.Http.Json;
using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Core.Contracts;
using Gebeta.Messaging;
using Gebeta.Payroll.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Gebeta.Api.Tests;

/// <summary>
/// ADR 0006: every module runs on its own with Core, so a customer can buy only what they need. Each combination starts the real
/// API on its own database: services validated strictly, every handler and event consumer resolved, migrations run, the main
/// endpoints answered; and where a module would use a partner, the flow works with the partner missing.
/// </summary>
public class ModuleIndependenceTests(GebetaApiFactory shared)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private sealed record Installed(List<InstalledModule> Modules);

    private sealed record InstalledModule(string Id);

    public static readonly string[] Business = ["accounting", "cash", "stock", "buying", "selling", "hr", "payroll"];

    /// <summary>The API with only <paramref name="modules"/> (Core is always there), on a database of its own.</summary>
    private sealed class ModulesFactory(string connectionString, string[] modules) : WebApplicationFactory<Program>
    {
        public IReadOnlyList<ServiceDescriptor> Registrations { get; private set; } = [];

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Main", connectionString);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Security:BootstrapAdmins:0", GebetaApiFactory.AdminUser);
            builder.UseSetting("RabbitMq:Enabled", "false");
            string[] enabled = ["core", .. modules];
            for (var i = 0; i < 20; i++)
            {
                builder.UseSetting($"Modules:Enabled:{i}", i < enabled.Length ? enabled[i] : string.Empty); // replace the configured list, not add to it
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                });
                Registrations = services.ToList();
            });
        }

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Strict: a registration whose dependencies only a missing module provides fails here, not in front of a customer.
            builder.UseDefaultServiceProvider(o => { o.ValidateOnBuild = true; o.ValidateScopes = true; });
            return base.CreateHost(builder);
        }

        public HttpClient Admin()
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, GebetaApiFactory.AdminUser);
            return client;
        }
    }

    private async Task<string> FreshDatabaseAsync(string name)
    {
        var builder = new NpgsqlConnectionStringBuilder(shared.ConnectionString);
        await using (var connection = new NpgsqlConnection(shared.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync(Ct);
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync(Ct);
        }

        builder.Database = name;
        return builder.ConnectionString;
    }

    /// <summary>Resolves every command handler, query handler and event consumer the combination registers.</summary>
    private static void ResolveEverything(ModulesFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var handlerTypes = factory.Registrations
            .Select(d => d.ServiceType)
            .Where(t => t.IsGenericType && (t.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) || t.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)))
            .Concat(scope.ServiceProvider.GetServices<EventSubscription>().Select(s => s.HandlerType))
            .Distinct()
            .ToList();
        Assert.NotEmpty(handlerTypes);
        foreach (var type in handlerTypes)
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(type));
        }
    }

    private static async Task<CompanyDto> CompanyAsync(HttpClient admin, string abbreviation, string tin)
    {
        var created = await admin.PostAsJsonAsync(U("/api/core/companies"), new { name = $"Alone {abbreviation}", abbreviation, legalForm = "PrivateLimitedCompany", tin }, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CompanyDto>(Ct))!;
    }

    /// <summary>A list endpoint each module answers on its own.</summary>
    private static string ListEndpoint(string module, Guid companyId) => module switch
    {
        "accounting" => $"/api/accounting/accounts?companyId={companyId}",
        "cash" => $"/api/cash/cashiers?companyId={companyId}",
        "stock" => $"/api/stock/warehouses?companyId={companyId}",
        "buying" => $"/api/buying/purchase-orders?companyId={companyId}&from=2026-01-01&to=2026-12-31",
        "selling" => $"/api/selling/sales-orders?companyId={companyId}&from=2026-01-01&to=2026-12-31",
        "hr" => $"/api/hr/leave-applications?companyId={companyId}&from=2026-01-01&to=2026-12-31",
        "payroll" => $"/api/payroll/structures?companyId={companyId}",
        _ => throw new ArgumentOutOfRangeException(nameof(module), module, null),
    };

    [Fact]
    public async Task Core_runs_alone()
    {
        await using var factory = new ModulesFactory(await FreshDatabaseAsync("alone_core"), []);
        ResolveEverything(factory);
        using var admin = factory.Admin();
        var modules = (await admin.GetFromJsonAsync<Installed>(U("/api/modules"), Ct))!;
        Assert.Equal(["core"], modules.Modules.Select(m => m.Id));
        var company = await CompanyAsync(admin, "ACO", "0800000301");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(U($"/api/core/items?companyId={company.Id}"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(U($"/api/stock/warehouses?companyId={company.Id}"), Ct)).StatusCode); // not installed

        // Alerts work with no module offering a condition: nothing to choose from, no rules, and a rule on a condition nobody offers is not found.
        Assert.Empty((await admin.GetFromJsonAsync<List<AlertConditionDto>>(U("/api/core/alerts/conditions"), Ct))!);
        Assert.Empty((await admin.GetFromJsonAsync<List<AlertRuleDto>>(U($"/api/core/alerts/rules?companyId={company.Id}"), Ct))!);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync(U("/api/core/alerts/rules"),
            new { companyId = company.Id, conditionId = "accounting.overdue-sales-invoices", userNames = new[] { GebetaApiFactory.AdminUser } }, Ct)).StatusCode);
    }

    [Theory]
    [InlineData("accounting", "0800000302")]
    [InlineData("cash", "0800000303")]
    [InlineData("stock", "0800000304")]
    [InlineData("buying", "0800000305")]
    [InlineData("selling", "0800000306")]
    [InlineData("hr", "0800000307")]
    [InlineData("payroll", "0800000308")]
    public async Task Each_module_runs_with_only_core(string module, string tin)
    {
        await using var factory = new ModulesFactory(await FreshDatabaseAsync($"alone_{module}"), [module]);
        ResolveEverything(factory);
        using var admin = factory.Admin();

        var modules = (await admin.GetFromJsonAsync<Installed>(U("/api/modules"), Ct))!;
        Assert.Equal(new[] { "core", module }.Order(StringComparer.Ordinal), modules.Modules.Select(m => m.Id).Order(StringComparer.Ordinal));
        var company = await CompanyAsync(admin, "A" + module[..2].ToUpperInvariant(), tin);
        var list = await admin.GetAsync(U(ListEndpoint(module, company.Id)), Ct);
        Assert.True(list.IsSuccessStatusCode, $"{module}: {(int)list.StatusCode} {await list.Content.ReadAsStringAsync(Ct)}");
        foreach (var other in Business.Where(b => b != module))
        {
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(U(ListEndpoint(other, company.Id)), Ct)).StatusCode);
        }
    }

    [Fact]
    public async Task All_modules_run_together()
    {
        await using var factory = new ModulesFactory(await FreshDatabaseAsync("alone_all"), Business);
        ResolveEverything(factory);
        using var admin = factory.Admin();
        var modules = (await admin.GetFromJsonAsync<Installed>(U("/api/modules"), Ct))!;
        Assert.Equal(Business.Length + 1, modules.Modules.Count);
    }

    /// <summary>Buying uses Stock (received) and Accounting (billed) when they are there; alone, an order simply shows nothing received or billed.</summary>
    [Fact]
    public async Task A_purchase_order_works_without_stock_or_accounting()
    {
        await using var factory = new ModulesFactory(await FreshDatabaseAsync("alone_buying_flow"), ["buying"]);
        using var admin = factory.Admin();
        var company = await CompanyAsync(admin, "ABF", "0800000309");
        var supplier = await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = company.Id, name = "Alone Supplier", type = "Supplier" }, Ct))
            .Content.ReadFromJsonAsync<PartyDto>(Ct);
        var item = await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = company.Id, code = "PAPER", name = "Paper", uom = "ream", isStockItem = true }, Ct))
            .Content.ReadFromJsonAsync<ItemDto>(Ct);
        var draft = await admin.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = company.Id, supplierId = supplier!.Id, orderDate = "2026-11-02",
            lines = new[] { new { itemId = item!.Id, qty = 10m, rate = 450m, warehouseId = (Guid?)null } },
        }, Ct);
        Assert.True(draft.IsSuccessStatusCode, await draft.Content.ReadAsStringAsync(Ct));
        var order = (await draft.Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
        var submitted = await admin.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/submit"), null, Ct);
        Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync(Ct));
        var shown = (await admin.GetFromJsonAsync<PurchaseOrderDto>(U($"/api/buying/purchase-orders/{order.Id}"), Ct))!;
        Assert.Equal((4_500m, 0m, 0m), (shown.NetTotal, shown.PercentReceived, shown.PercentBilled));
    }

    /// <summary>Payroll uses HR's attendance and leave for payment days when HR is there; alone, a slip pays the full month.</summary>
    [Fact]
    public async Task A_salary_slip_works_without_hr_or_accounting()
    {
        await using var factory = new ModulesFactory(await FreshDatabaseAsync("alone_payroll_flow"), ["payroll"]);
        using var admin = factory.Admin();
        var company = await CompanyAsync(admin, "APF", "0800000310");
        var structure = await (await admin.PostAsJsonAsync(U("/api/payroll/structures"), new { companyId = company.Id, name = "Standard" }, Ct))
            .Content.ReadFromJsonAsync<SalaryStructureDto>(Ct);
        var employee = await (await admin.PostAsJsonAsync(U("/api/core/employees"),
            new { companyId = company.Id, firstName = "Selam", fatherName = "Worku", gender = "Female", dateOfJoining = "2025-01-01" }, Ct)).Content.ReadFromJsonAsync<EmployeeDto>(Ct);
        var assigned = await admin.PostAsJsonAsync(U("/api/payroll/assignments"), new { employeeId = employee!.Id, structureId = structure!.Id, @base = 13_000m, fromDate = "2025-01-01" }, Ct);
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        var slip = await admin.PostAsJsonAsync(U("/api/payroll/salary-slips"), new { employeeId = employee.Id, periodStart = "2026-10-01", periodEnd = "2026-10-31" }, Ct);
        Assert.True(slip.IsSuccessStatusCode, await slip.Content.ReadAsStringAsync(Ct));
        var paid = (await slip.Content.ReadFromJsonAsync<SalarySlipDto>(Ct))!;
        Assert.Equal((30m, 9_540m), (paid.PaymentDays, paid.NetPay)); // 13,000 − tax 2,550 − pension 910
        var submitted = await admin.PostAsync(U($"/api/payroll/salary-slips/{paid.Id}/submit"), null, Ct);
        Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync(Ct)); // the journal waits in the outbox for an Accounting that is not installed
    }
}
