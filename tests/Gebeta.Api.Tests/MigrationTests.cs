using System.Net.Http.Json;
using System.Text.Json;
using Gebeta.Accounting.Application;
using Gebeta.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Gebeta.Accounting.Contracts;
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
            new("Cement 50 kg", "Cement 50 kg bag", "Bag", "Building", true, false, "FIFO"),
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

        public List<ErpAccountBalance> Balances { get; } = [];
        public List<ErpOutstanding> Outstanding { get; } = [];
        public List<ErpStockBalance> Stock { get; } = [];
        public DateOnly? YearStartAsked { get; private set; }

        public Task<IReadOnlyList<ErpAccountBalance>> TrialBalanceAsync(string c, DateOnly asOf, DateOnly yearStart, CancellationToken cancellationToken)
        {
            YearStartAsked = yearStart;
            return Task.FromResult<IReadOnlyList<ErpAccountBalance>>(Balances);
        }

        public Task<IReadOnlyList<ErpOutstanding>> OutstandingAsync(string c, DateOnly asOf, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpOutstanding>>(Outstanding);
        public Task<IReadOnlyList<ErpStockBalance>> StockBalancesAsync(string c, DateOnly asOf, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpStockBalance>>(Stock);

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

    /// <summary>
    /// The opening stock's value journals wait in the Stock outbox (no RabbitMQ in these tests); deliver this company's to Accounting
    /// the way the running server would.
    /// </summary>
    private async Task DeliverStockJournalsAsync(Guid companyId)
    {
        var pending = new List<StockJournalRequested>();
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(
                $"SELECT payload::text FROM stock.{MessagingModel.OutboxTable} WHERE processed_at IS NULL AND clr_type LIKE '%StockJournalRequested%'", connection);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                var e = JsonSerializer.Deserialize<StockJournalRequested>(reader.GetString(0), MessagingModel.Json)!;
                if (e.CompanyId == companyId)
                {
                    pending.Add(e);
                }
            }
        }

        foreach (var e in pending)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IAccountingUnitOfWork>();
            await using var transaction = await unitOfWork.BeginTransactionAsync(Ct);
            await scope.ServiceProvider.GetRequiredService<StockJournalRequestedConsumer>().HandleAsync(e, Ct);
            await transaction.CommitAsync(Ct);
        }
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

    [Fact]
    public async Task Cut_over_brings_open_invoices_stock_and_the_trial_balance_and_proves_them_against_erpnext()
    {
        const string name = "Kessem Sugar Factory";
        var cutover = new DateOnly(2026, 7, 7); // Sene 30, 2018: the last day of a fiscal year
        var erp = new FakeErpNext(name, "0800000190");
        erp.Stock.AddRange(
        [
            new("Cement 50 kg", "Stores - KES", 100m, 52_500m, [new(60m, 500m), new(40m, 562.5m)]),
            new("SUGAR-WHITE", "Finished Goods - KES", 20m, 70_000.23m, []),
        ]);
        erp.Outstanding.AddRange(
        [
            new("Receivable", "Customer", "CUST-0001", "Sales Invoice", "SINV-0001", 11_500m, new(2026, 5, 10), new(2026, 6, 9)),
            new("Receivable", "Customer", "Merkato Traders", "Sales Invoice", "SINV-0002", 2_300m, new(2026, 7, 1), new(2026, 7, 31)),
            new("Payable", "Supplier", "Addis Cement Factory", "Purchase Invoice", "PINV-0001", 34_500m, new(2026, 6, 20), new(2026, 7, 20)),
            new("Receivable", "Customer", "CUST-0002", "Payment Entry", "PE-0001", -1_000m, new(2026, 6, 1), null),
            new("Receivable", "Employee", "HR-EMP-0001", "Journal Entry", "JV-0001", 800m, new(2026, 6, 1), null),
        ]);
        erp.Balances.AddRange(
        [
            new("Debtors - KES", "Asset", "Receivable", null, 13_600m, 0m),           // 11,500 + 2,300 - 1,000 advance + 800 employee
            new("Creditors - KES", "Liability", "Payable", null, -34_500m, 0m),
            new("Stock In Hand - KES", "Asset", "Stock", null, 122_500.23m, 0m),
            new("Commercial Bank - KES", "Asset", "Bank", null, 299_200m, 0m),
            new("Output VAT - KES", "Liability", "Tax", null, -5_000m, 0m),
            new("Loan from Bank - KES", "Liability", null, null, -100_000m, 0m),
            new("Capital Stock - KES", "Equity", null, null, -250_000m, 0m),
            new("Sales - KES", "Income", null, null, -400_000m, -150_000m),
            new("Cost of Goods Sold - KES", "Expense", "Cost of Goods Sold", null, 300_000m, 100_000m),
            new("Office Rent - KES", "Expense", null, null, 54_199.77m, 20_000m),
        ]);
        Assert.Equal(0m, erp.Balances.Sum(b => b.Balance));
        var map = Path.Combine(Path.GetTempPath(), $"gebeta-accounts-{Guid.NewGuid():N}.csv");
        var options = new MigrationOptions(name, Cutover: cutover, AccountMapPath: map);
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);

        try
        {
            // The dry run checks the cut-over steps against the masters that would be created, and writes the account map
            // with suggestions for review (a file, not Gebeta data).
            var dry = await Run(erp, options with { DryRun = true });
            Assert.True(dry.Single(r => r.Name == "invoices").Ok);
            Assert.True(dry.Single(r => r.Name == "stock").Ok);
            Assert.Contains(dry.Single(r => r.Name == "opening").Failures, f => f.Contains("Review it", StringComparison.Ordinal));

            // First run: everything but the opening balances, which wait for the one account without a code.
            var first = await Run(erp, options);
            Assert.All(first.Where(r => r.Name != "opening"), r => Assert.True(r.Ok, r.Name + ": " + string.Join("; ", r.Failures)));
            var invoices = first.Single(r => r.Name == "invoices");
            Assert.Equal(3, invoices.Created);
            Assert.Contains(invoices.Warnings, w => w.Contains("advances total 1,000.00", StringComparison.Ordinal));
            Assert.Contains(invoices.Warnings, w => w.Contains("JV-0001", StringComparison.Ordinal));
            Assert.Equal("Loan from Bank - KES (Liability, balance -100,000.00): no Gebeta account in " + map + ".", Assert.Single(first.Single(r => r.Name == "opening").Failures));
            Assert.Equal(new DateOnly(2025, 7, 8), erp.YearStartAsked); // Hamle 1, 2017: the fiscal year of the cut-over day

            var suggested = AccountMap.Read(map);
            Assert.Null(suggested["Loan from Bank - KES"]);  // a loan is the accountant's call
            Assert.Equal(("1200", "1300", "4100", "6200", "5000", "1120"),
                (suggested["Debtors - KES"], suggested["Stock In Hand - KES"], suggested["Sales - KES"], suggested["Office Rent - KES"], suggested["Cost of Goods Sold - KES"], suggested["Commercial Bank - KES"]));

            // The accountant maps the loan to a new account, which the chart does not have yet.
            await File.WriteAllTextAsync(map, (await File.ReadAllTextAsync(map, Ct)).Replace("Loan from Bank - KES,Liability,,-100000.00,", "Loan from Bank - KES,Liability,,-100000.00,2500", StringComparison.Ordinal), Ct);
            var notYet = await Run(erp, options with { Steps = ["opening"] });
            Assert.Contains(notYet.Single(r => r.Name == "opening").Failures, f => f.Contains("2500 is not a postable", StringComparison.Ordinal));

            var company = (await admin.GetFromJsonAsync<List<CompanyDto>>(U("/api/core/companies"), Ct))!.Single(c => c.Name == name);
            var account = await admin.PostAsJsonAsync(U("/api/accounting/accounts"), new { companyId = company.Id, code = "2500", name = "Bank Loans", parentCode = "2000" }, Ct);
            Assert.True(account.IsSuccessStatusCode, await account.Content.ReadAsStringAsync(Ct));

            // Posted, but the opening stock's journals have not reached the ledger yet: the check says so instead of passing.
            var waiting = (await Run(erp, options with { Steps = ["opening"], SettleWait = TimeSpan.Zero })).Single(r => r.Name == "opening");
            Assert.Equal(1, waiting.Created);
            Assert.Contains("run --steps opening again", waiting.Check, StringComparison.Ordinal);

            await DeliverStockJournalsAsync(company.Id);
            var opening = (await Run(erp, options with { Steps = ["opening"] })).Single(r => r.Name == "opening");
            Assert.True(opening.Ok, string.Join("; ", opening.Failures));
            Assert.Equal(1, opening.AlreadyMigrated);
            Assert.Equal("trial balance at 2026-07-07: every account equals ERPNext", opening.Check);
            Assert.Contains(waiting.Warnings, w => w.Contains("a profit of 30,000.00", StringComparison.Ordinal));

            var tb = (await admin.GetFromJsonAsync<TrialBalanceDto>(U($"/api/accounting/reports/trial-balance?companyId={company.Id}&asOf=2026-07-07"), Ct))!;
            decimal Net(string code) => tb.Rows.Where(r => r.Code == code).Sum(r => r.Debit - r.Credit);
            Assert.Equal(122_500.23m, Net("1300"));     // the opening stock, met by the stock accounts through 3900
            Assert.Equal(0m, Net("3900"));
            Assert.Equal(13_600m, Net("1200"));
            Assert.Equal(-100_000m, Net("2500"));
            Assert.Equal(-30_000m, Net("3200"));       // earlier years' profit
            Assert.Equal(-250_000m, Net("4100"));      // this year's sales only
            Assert.Equal(34_199.77m, Net("6200"));

            var stock = (await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={company.Id}&asOf=2026-07-07"), Ct))!;
            Assert.Equal(122_500.23m, stock.TotalValue);
            Assert.Equal("Fifo", stock.Rows.Single(r => r.ItemCode == "CEMENT-50-KG").Method);

            var sales = (await admin.GetFromJsonAsync<List<InvoiceDto>>(U($"/api/accounting/invoices?companyId={company.Id}&kind=Sales&from=2026-01-01&to=2026-07-07"), Ct))!;
            Assert.Equal(13_800m, sales.Where(i => i.IsOpening).Sum(i => i.Outstanding));

            // Running everything again changes nothing and every check stays green.
            var again = await Run(erp, options);
            Assert.All(again, r => Assert.True(r.Ok, r.Name + ": " + string.Join("; ", r.Failures)));
            Assert.All(again, r => Assert.Equal(0, r.Created));
        }
        finally
        {
            File.Delete(map);
        }
    }

    [Fact]
    public void A_fifo_queue_becomes_layers_that_add_up_to_erpnexts_value_to_the_cent()
    {
        var layers = MigrationRun.Layers(new ErpStockBalance("X", "W", 3m, 10m, [new(1m, 3.333m), new(1m, 3.333m), new(1m, 3.333m)]));
        Assert.Equal([(1m, 3.33m), (1m, 3.33m), (1m, 3.34m)], layers);

        // A queue that does not match the quantity (ERPNext after negative stock) becomes one line at the average.
        Assert.Equal([(5m, 50m)], MigrationRun.Layers(new ErpStockBalance("X", "W", 5m, 50m, [new(2m, 10m), new(2m, 10m)])));
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
