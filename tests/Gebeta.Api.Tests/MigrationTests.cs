using System.Net.Http.Json;
using Gebeta.Core.Contracts;
using Gebeta.Migration;
using Gebeta.Stock.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>The ERPNext migration tool (docs/data-migration.md) against a fake ERPNext with the usual untidy data.</summary>
public class MigrationTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private sealed class FakeErpNext(string company, string tin) : IErpNextSource
    {
        public List<ErpParty> Customers { get; } =
        [
            new("CUST-0001", "Bole Builders", "0800000161", false),
            new("CUST-0002", "Habesha Hotel", "TIN 12-34", false),   // not a valid TIN
            new("CUST-0003", "Closed Shop", null, true),            // disabled
            new("Merkato Traders", "Merkato Traders", null, false), // also a supplier
        ];

        public List<ErpParty> Suppliers { get; } =
        [
            new("Addis Cement Factory", "Addis Cement Factory", "0800000162", false),
            new("Merkato Traders", "Merkato Traders", "0800000163", false),
        ];

        public List<ErpItem> Items { get; } =
        [
            new("Cement 50 kg", "Cement 50 kg bag", "Bag", "Building", true, false),
            new("SUGAR-WHITE", "White sugar", "Quintal", "Products", true, false),
            new("Transport Service", "Transport", "Trip", "Services", false, false),
            new("OLD-ITEM", "Old item", "Nos", null, true, true),     // disabled
        ];

        public List<ErpWarehouse> Warehouses { get; } =
        [
            new($"Stores - {company[..3].ToUpperInvariant()}", "Stores", false),
            new($"Finished Goods - {company[..3].ToUpperInvariant()}", "Finished Goods", false),
            new($"Old Store - {company[..3].ToUpperInvariant()}", "Old Store", true),
        ];

        public Task<ErpCompany?> CompanyAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult(name == company ? new ErpCompany(company, company[..3].ToUpperInvariant() + "X", tin, "ETB") : null);

        public Task<IReadOnlyList<ErpParty>> CustomersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpParty>>(Customers);
        public Task<IReadOnlyList<ErpParty>> SuppliersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpParty>>(Suppliers);
        public Task<IReadOnlyList<ErpItem>> ItemsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpItem>>(Items);
        public Task<IReadOnlyList<ErpWarehouse>> WarehousesAsync(string c, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpWarehouse>>(Warehouses);
    }

    private async Task<IReadOnlyList<StepReport>> Run(IErpNextSource source, MigrationOptions options)
    {
        await using var services = MigrationHost.Build(factory.ConnectionString);
        return await new MigrationRun(services, source, new IdMap(factory.ConnectionString), options).RunAsync(Ct);
    }

    [Fact]
    public async Task A_dry_run_writes_nothing_and_a_real_run_migrates_once_with_its_checks_green()
    {
        const string name = "Wonji Shoa Sugar Factory";
        var erp = new FakeErpNext(name, "0800000160");
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        async Task<CompanyDto?> Company() => (await admin.GetFromJsonAsync<List<CompanyDto>>(U("/api/core/companies"), Ct))!.SingleOrDefault(c => c.Name == name);

        // Dry run: every record checked with the real rules, nothing written.
        var dry = await Run(erp, new MigrationOptions(name, DryRun: true));
        Assert.All(dry, r => Assert.True(r.Ok, string.Join("; ", r.Failures)));
        Assert.Equal(4, dry.Single(r => r.Name == "parties").Created);   // Merkato once, as both; the disabled customer left behind
        Assert.Null(await Company());

        // Real run.
        var reports = await Run(erp, new MigrationOptions(name));
        Assert.All(reports, r => Assert.True(r.Ok, string.Join("; ", r.Failures)));
        var company = await Company();
        Assert.Equal(("0800000160", "WONX"), (company!.Tin, company.Abbreviation));

        var parties = await admin.GetFromJsonAsync<List<PartyDto>>(U($"/api/core/parties?companyId={company.Id}"), Ct);
        Assert.Equal(4, parties!.Count);
        Assert.Equal("Both", parties.Single(p => p.Name == "Merkato Traders").Type);
        Assert.Equal("0800000163", parties.Single(p => p.Name == "Merkato Traders").Tin);
        Assert.Null(parties.Single(p => p.Name == "Habesha Hotel").Tin);
        Assert.Contains(reports.Single(r => r.Name == "parties").Warnings, w => w.Contains("Habesha Hotel", StringComparison.Ordinal));

        var items = await admin.GetFromJsonAsync<List<ItemDto>>(U($"/api/core/items?companyId={company.Id}"), Ct);
        Assert.Equal(["CEMENT-50-KG", "SUGAR-WHITE", "TRANSPORT-SERVICE"], items!.Select(i => i.Code).Order().ToArray());
        Assert.False(items!.Single(i => i.Code == "TRANSPORT-SERVICE").IsStockItem);

        var warehouses = await admin.GetFromJsonAsync<List<WarehouseDto>>(U($"/api/stock/warehouses?companyId={company.Id}"), Ct);
        Assert.Equal(["FINISHED-GOODS", "STORES"], warehouses!.Select(w => w.Code).Order().ToArray());
        Assert.Contains(reports, r => r.Check == "3 of 3 active ERPNext records are in Gebeta");

        // Run again: everything is already migrated, nothing doubles.
        var again = await Run(erp, new MigrationOptions(name));
        Assert.All(again, r => Assert.True(r.Ok));
        Assert.All(again, r => Assert.Equal(0, r.Created));
        Assert.Equal(4, again.Single(r => r.Name == "parties").AlreadyMigrated);
        Assert.Equal(4, (await admin.GetFromJsonAsync<List<PartyDto>>(U($"/api/core/parties?companyId={company.Id}"), Ct))!.Count);

        // A new customer in ERPNext comes over on the next run, alone.
        erp.Customers.Add(new("CUST-0004", "Adama Bakery", null, false));
        var later = await Run(erp, new MigrationOptions(name, Steps: ["parties"]));
        Assert.Equal(1, later.Single(r => r.Name == "parties").Created);
    }

    [Fact]
    public async Task Problems_are_reported_per_record_and_fail_the_check_without_stopping_the_run()
    {
        const string name = "Metehara Sugar Factory";
        var erp = new FakeErpNext(name, "0800000170");
        erp.Items.Add(new("Cement-50-kg", "Same code after normalising", "Bag", null, true, false));

        var reports = await Run(erp, new MigrationOptions(name));

        var items = reports.Single(r => r.Name == "items");
        Assert.False(items.Ok);
        Assert.Single(items.Failures);
        Assert.Contains("CEMENT-50-KG", items.Failures[0], StringComparison.Ordinal);
        Assert.Equal(3, items.Created);
        Assert.False(items.CheckPassed);                        // 3 of 4 in Gebeta
        Assert.True(reports.Single(r => r.Name == "warehouses").Ok); // later steps still ran
    }

    [Fact]
    public async Task A_company_without_a_valid_tin_stops_with_a_clear_message_and_can_target_an_existing_company()
    {
        const string name = "Tendaho Sugar Factory";
        var erp = new FakeErpNext(name, "not-a-tin");
        var refused = await Run(erp, new MigrationOptions(name));
        Assert.Contains("--tin", Assert.Single(refused).Failures.Single(), StringComparison.Ordinal);

        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var existing = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Tendaho (set up in Gebeta)", abbreviation = "TND", legalForm = "PublicEnterprise", tin = "0800000180" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);
        var reports = await Run(erp, new MigrationOptions(name, TargetCompanyId: existing!.Id, Steps: ["parties"]));
        Assert.Equal(1, reports.Single(r => r.Name == "company").Adopted);
        Assert.Equal(4, (await admin.GetFromJsonAsync<List<PartyDto>>(U($"/api/core/parties?companyId={existing.Id}"), Ct))!.Count);
    }

    [Theory]
    [InlineData("Stores - ADSF", 20, "STORES")]
    [InlineData("Work In Progress - ADSF", 20, "WORK-IN-PROGRESS")]
    [InlineData("Cement 50 kg", 40, "CEMENT-50-KG")]
    [InlineData("Raw sugar (bulk) / grade A", 40, "RAW-SUGAR-BULK-GRADE-A")]
    [InlineData("A very long warehouse name indeed", 20, "A-VERY-LONG-WAREHOUS")]
    public void Codes_are_normalised_the_same_way_every_run(string name, int max, string expected) =>
        Assert.Equal(expected, Codes.Normalise(name, max));
}
