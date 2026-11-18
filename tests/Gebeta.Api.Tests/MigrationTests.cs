using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gebeta.Accounting.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Gebeta.Accounting.Contracts;
using Gebeta.Core.Contracts;
using Gebeta.Selling.Contracts;
using Gebeta.Buying.Contracts;
using Gebeta.Hr.Contracts;
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
        public List<ErpEmployee> Employees { get; } = [];
        public List<ErpSalaryStructure> Structures { get; } = [];
        public List<ErpAssignment> Assignments { get; } = [];
        public List<ErpSalarySlip> Slips { get; } = [];
        public ErpFormulaScope Scope { get; set; } = ErpFormulaScope.Empty;
        public Dictionary<string, Dictionary<string, Dictionary<string, string?>>> Fields { get; } = new(); // doctype → record → field → value

        public Task<ErpFormulaScope> FormulaScopeAsync(CancellationToken cancellationToken) => Task.FromResult(Scope);

        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>> FieldValuesAsync(string doctype, string c, IReadOnlyCollection<string> fields,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>>(
                Fields.GetValueOrDefault(doctype, []).ToDictionary(r => r.Key, r => (IReadOnlyDictionary<string, string?>)r.Value));

        public Task<IReadOnlyList<ErpEmployee>> EmployeesAsync(string c, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpEmployee>>(Employees);
        public Task<IReadOnlyList<ErpSalaryStructure>> SalaryStructuresAsync(string c, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpSalaryStructure>>(Structures);
        public Task<IReadOnlyList<ErpAssignment>> AssignmentsAsync(string c, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpAssignment>>(Assignments);
        public List<ErpSalarySlip> History { get; } = []; // slips before the latest payroll
        public List<ErpLeaveBalance> Leaves { get; } = [];
        public List<ErpOrderLine> Orders { get; } = [];
        public List<ErpUser> Users { get; } = [];

        public Task<IReadOnlyList<ErpUser>> UsersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpUser>>(Users);

        public Task<IReadOnlyList<ErpOrderLine>> OpenOrderLinesAsync(string c, DateOnly asOf, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ErpOrderLine>>(Orders);

        public Task<IReadOnlyList<ErpLeaveBalance>> LeaveBalancesAsync(string c, DateOnly asOf, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ErpLeaveBalance>>(Leaves);

        public Task<IReadOnlyList<ErpSalarySlip>> SlipsAsync(string c, DateOnly? from, DateOnly to, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ErpSalarySlip>>(History.Concat(Slips).Where(s => s.EndDate <= to && (from is null || s.StartDate >= from)).ToList());

        public Task<IReadOnlyList<ErpSalarySlip>> LatestSlipsAsync(string c, DateOnly? asOf, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ErpSalarySlip>>(Slips);

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
        await using var services = MigrationHost.Build(factory.ConnectionString, modules: options.Modules);
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
        erp.Items.Add(new("Cement-50-kg", "Same code after normalising", "Bag", null, true, false)); // a clash: gets a suffix
        erp.Items.Add(new("BLANK", "", "Nos", null, true, false));                                  // no name: refused

        var reports = await Run(erp, new MigrationOptions(name));

        var items = reports.Single(r => r.Name == "items");
        Assert.False(items.Ok);
        Assert.StartsWith("BLANK: ", Assert.Single(items.Failures), StringComparison.Ordinal);
        Assert.Contains(items.Warnings, w => w == "Cement-50-kg: CEMENT-50-KG is taken by another item; this one becomes CEMENT-50-KG-2.");
        Assert.Equal(4, items.Created);
        Assert.False(items.CheckPassed);                        // 4 of 5 in Gebeta
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
            new("Receivable", "Customer", "CUST-0003", "Sales Invoice", "SINV-0003", 5_000m, new(2026, 3, 1), new(2026, 3, 31)), // disabled, still owes
            new("Receivable", "Employee", "HR-EMP-0001", "Journal Entry", "JV-0001", 800m, new(2026, 6, 1), null),
        ]);
        erp.Balances.AddRange(
        [
            new("Debtors - KES", "Asset", "Receivable", null, 18_600m, 0m),           // 11,500 + 2,300 + 5,000 - 1,000 advance + 800 employee
            new("Creditors - KES", "Liability", "Payable", null, -34_500m, 0m),
            new("Stock In Hand - KES", "Asset", "Stock", null, 122_500.23m, 0m),
            new("Commercial Bank - KES", "Asset", "Bank", null, 294_200m, 0m),
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
            Assert.Equal(4, invoices.Created);
            Assert.Contains(first.Single(r => r.Name == "parties").Warnings, w => w.StartsWith("Closed Shop (CUST-0003): disabled", StringComparison.Ordinal));
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
            Assert.Equal(18_600m, Net("1200"));
            Assert.Equal(-100_000m, Net("2500"));
            Assert.Equal(-30_000m, Net("3200"));       // earlier years' profit
            Assert.Equal(-250_000m, Net("4100"));      // this year's sales only
            Assert.Equal(34_199.77m, Net("6200"));

            var stock = (await admin.GetFromJsonAsync<StockBalanceDto>(U($"/api/stock/reports/balance?companyId={company.Id}&asOf=2026-07-07"), Ct))!;
            Assert.Equal(122_500.23m, stock.TotalValue);
            Assert.Equal("Fifo", stock.Rows.Single(r => r.ItemCode == "CEMENT-50-KG").Method);

            var sales = (await admin.GetFromJsonAsync<List<InvoiceDto>>(U($"/api/accounting/invoices?companyId={company.Id}&kind=Sales&from=2026-01-01&to=2026-07-07"), Ct))!;
            Assert.Equal(18_800m, sales.Where(i => i.IsOpening).Sum(i => i.Outstanding));

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
    public async Task Employees_structures_and_assignments_come_over_and_gebeta_recalculates_the_last_payroll_to_the_cent()
    {
        const string name = "Beles Sugar Factory";
        var erp = new FakeErpNext(name, "0800000200");
        erp.Employees.AddRange(
        [
            new("HR-EMP-0001", "Abebe", "Kebede", "Tesfaye", "Abebe Kebede Tesfaye", "Male", new(2020, 1, 15), "Active", null, "0800000201"),
            new("HR-EMP-0002", "Almaz", null, null, "Almaz Tadesse Bekele", "Female", new(2025, 7, 1), "Active", null, "123"),
            new("HR-EMP-0003", "Lemma", "Girma", null, "Lemma Girma", "Male", new(2019, 3, 1), "Left", new(2024, 6, 30), null),
        ]);

        // ERPNext rows: Parent, IsDeduction, Component, Abbr, Formula, Amount, Condition, IsTaxable, DependsOnPaymentDays, Statistical, NotInTotal, TaxSlabBased
        static ErpSalaryDetail Row(string parent, bool deduction, string component, string abbr, string? formula, decimal amount = 0m, bool taxable = true,
            bool days = false, bool notInTotal = false, bool tax = false, decimal additional = 0m, bool fromAdditional = false) =>
            new(parent, deduction, component, abbr, formula, amount, null, taxable, days, false, notInTotal, tax, additional, fromAdditional);
        ErpSalaryDetail[] Staff(string p) =>
        [
            Row(p, false, "Basic Salary", "BS", "base", days: true),
            Row(p, false, "Transport Allowance", "TA", null, 600m, taxable: false),
            Row(p, true, "Income Tax", "IT", null, tax: true),
            Row(p, true, "Employee Pension", "PE", "BS * 0.07"),
            Row(p, true, "Employer Pension", "EPC", "BS * 0.11", notInTotal: true),
        ];
        erp.Structures.AddRange([new("Staff 2018", Staff("Staff 2018")), new("Old Grade", [Row("Old Grade", false, "Basic Salary", "BS", "base"), Row("Old Grade", true, "Income Tax", "IT", null, tax: true)])]);

        // A structure whose formulas read the employee's ctc field, and a component (OLD) from another structure, which ERPNext counts as 0.
        erp.Structures.Add(new("Contract 2018",
        [
            Row("Contract 2018", false, "Basic Salary", "BS", "ctc", days: true),
            Row("Contract 2018", false, "Old Bonus", "XB", "OLD * 2"),
            Row("Contract 2018", true, "Income Tax", "IT", null, tax: true),
        ]));
        erp.Scope = new ErpFormulaScope(new HashSet<string>(), new HashSet<string>(["ctc", "first_name"]), new HashSet<string>(), new HashSet<string>(["OLD", "BS", "IT"]));
        erp.Fields["Employee"] = new() { ["HR-EMP-0002"] = new() { ["ctc"] = "7000.000000000" } };
        erp.Assignments.AddRange(
        [
            new("HR-EMP-0001", "Old Grade", 8_000m, new(2024, 7, 8)),
            new("HR-EMP-0001", "Staff 2018", 10_000m, new(2025, 7, 8)),
            new("HR-EMP-0002", "Staff 2018", 6_000m, new(2025, 7, 8)),
            new("HR-EMP-0001", "Staff 2018", 11_000m, new(2026, 6, 8), Created: new(2026, 8, 3, 9, 0, 0)), // a raise for Sene, entered after Sene was paid
            new("HR-EMP-0002", "Contract 2018", 0m, new(2026, 7, 8)),
            new("HR-EMP-0003", "Old Grade", 5_000m, new(2024, 1, 1)),
        ]);

        // Sene 2018 as ERPNext paid it, worked out by hand with the 2025 table:
        //   0001: BS 10,000 + TA 600 (not taxable) + OT 1,500 = 12,100; tax on 11,500 = 30% - 1,350 = 2,100; PE 700; net 9,300; EPC 1,100.
        //   0002: 20 of 30 days, BS 4,000 + TA 600 = 4,600; tax on 4,000 = 15% - 300 = 300; PE 280; net 4,020; EPC 440.
        DateOnly start = new(2026, 6, 8), end = new(2026, 7, 7);
        erp.Slips.AddRange(
        [
            new("Sal Slip/HR-EMP-0001/00012", "HR-EMP-0001", "Staff 2018", start, end, 30m, 30m, 12_100m, 9_300m, Created: new(2026, 7, 8, 10, 0, 0), Rows:
            [
                Row("s1", false, "Basic Salary", "BS", null, 10_000m), Row("s1", false, "Transport Allowance", "TA", null, 600m, taxable: false),
                Row("s1", false, "Overtime", "OT", null, 1_500m, additional: 1_500m, fromAdditional: true),
                Row("s1", true, "Income Tax", "IT", null, 2_100m), Row("s1", true, "Employee Pension", "PE", null, 700m),
                Row("s1", true, "Employer Pension", "EPC", null, 1_100m, notInTotal: true),
            ]),
            new("Sal Slip/HR-EMP-0002/00001", "HR-EMP-0002", "Staff 2018", start, end, 20m, 30m, 4_600m, 4_020m,
            [
                Row("s2", false, "Basic Salary", "BS", null, 4_000m), Row("s2", false, "Transport Allowance", "TA", null, 600m, taxable: false),
                Row("s2", true, "Income Tax", "IT", null, 300m), Row("s2", true, "Employee Pension", "PE", null, 280m),
                Row("s2", true, "Employer Pension", "EPC", null, 440m, notInTotal: true),
            ]),
        ]);

        var dry = await Run(erp, new MigrationOptions(name, DryRun: true));
        Assert.All(dry, r => Assert.True(r.Ok, r.Name + ": " + string.Join("; ", r.Failures)));
        Assert.Equal("payroll 2026-06-08 – 2026-07-07: 2 of 2 slips recalculated by Gebeta equal ERPNext to the cent", dry.Single(r => r.Name == "payroll").Check);

        var reports = await Run(erp, new MigrationOptions(name));
        Assert.All(reports, r => Assert.True(r.Ok, r.Name + ": " + string.Join("; ", r.Failures)));
        Assert.Equal((2, 3, 5), (reports.Single(r => r.Name == "employees").Created, reports.Single(r => r.Name == "structures").Created, reports.Single(r => r.Name == "assignments").Created));
        Assert.Contains(reports.Single(r => r.Name == "employees").Warnings, w => w.Contains("1 employees who left", StringComparison.Ordinal));
        var structureWarnings = reports.Single(r => r.Name == "structures").Warnings;
        Assert.Contains(structureWarnings, w => w.Contains("Employer Pension (EPC)", StringComparison.Ordinal));
        Assert.Contains(structureWarnings, w => w.Contains("Old Bonus (XB): 'OLD' is a salary component outside this structure", StringComparison.Ordinal));
        Assert.Contains(structureWarnings, w => w.Contains("\"Contract 2018\": 'ctc' is read from the employee record", StringComparison.Ordinal));
        Assert.Equal("payroll 2026-06-08 – 2026-07-07: 2 of 2 slips recalculated by Gebeta equal ERPNext to the cent", reports.Single(r => r.Name == "payroll").Check);

        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = (await admin.GetFromJsonAsync<List<CompanyDto>>(U("/api/core/companies"), Ct))!.Single(c => c.Name == name);
        var employees = (await admin.GetFromJsonAsync<List<EmployeeDto>>(U($"/api/core/employees?companyId={company.Id}"), Ct))!;
        var almaz = employees.Single(e => e.FirstName == "Almaz");
        Assert.Equal(("Tadesse", "Bekele"), (almaz.FatherName, almaz.GrandfatherName)); // from the full name
        var contract = (await admin.GetFromJsonAsync<List<SalaryStructureDto>>(U($"/api/payroll/structures?companyId={company.Id}"), Ct))!.Single(s => s.Name == "Contract 2018");
        Assert.Equal(["ctc"], contract.Variables);
        Assert.Equal("0 * 2", contract.Components.Single(c => c.Abbreviation == "XB").Formula);
        var almazNow = (await admin.GetFromJsonAsync<List<SalaryStructureAssignmentDto>>(U($"/api/payroll/employees/{almaz.Id}/assignments"), Ct))!.MaxBy(a => a.FromDate)!;
        Assert.Equal(7_000m, almazNow.Values!["ctc"]);
        var history = (await admin.GetFromJsonAsync<List<SalaryStructureAssignmentDto>>(U($"/api/payroll/employees/{employees.Single(e => e.FirstName == "Abebe").Id}/assignments"), Ct))!;
        Assert.Equal([8_000m, 10_000m, 11_000m], history.OrderBy(a => a.FromDate).Select(a => a.Base));
        Assert.Contains(reports.Single(r => r.Name == "payroll").Warnings,
            w => w == "Sal Slip/HR-EMP-0001/00012 (HR-EMP-0001): paid on base 10,000.00; the base from 2026-06-08 was later set to 11,000.00, so back pay is due.");

        // Sene came over as history: lines as paid, old numbers, nothing posted.
        Assert.Equal("2 of 2 slips (this run: net pay 13,320.00, equal to ERPNext slip by slip)", reports.Single(r => r.Name == "history").Check);
        var slips = (await admin.GetFromJsonAsync<List<SalarySlipDto>>(U($"/api/payroll/salary-slips?companyId={company.Id}"), Ct))!;
        Assert.All(slips, s => Assert.True(s.IsHistory));
        var abebeSene = slips.Single(s => s.Number == "Sal Slip/HR-EMP-0001/00012");
        Assert.Equal(("Submitted", 12_100m, 9_300m, 1_100m), (abebeSene.Status, abebeSene.GrossPay, abebeSene.NetPay, abebeSene.EmployerContributions));
        await using (var connection = new NpgsqlConnection(factory.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await using var outbox = new NpgsqlCommand(
                $"SELECT count(*) FROM payroll.{MessagingModel.OutboxTable} WHERE payload::text LIKE '%' || @slip || '%'", connection);
            outbox.Parameters.AddWithValue("slip", abebeSene.Id.ToString());
            Assert.Equal(0L, (long)(await outbox.ExecuteScalarAsync(Ct))!); // no journal: the opening balances hold those months
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync(U($"/api/payroll/salary-slips/{abebeSene.Id}/cancel"), null, Ct)).StatusCode);

        // The raise entered after Sene was paid becomes a back payment against the history slip, overtime included:
        // paid 10,000 + 600 + 1,500 OT (tax on 11,500 = 2,100); now 11,000 + 600 + 1,500 (tax on 12,500 = 2,400), pension 770.
        var abebe = employees.Single(e => e.FirstName == "Abebe");
        var raise = (await admin.GetFromJsonAsync<List<SalaryStructureAssignmentDto>>(U($"/api/payroll/employees/{abebe.Id}/assignments"), Ct))!
            .Single(a => a.Base == 11_000m);
        var backPay = await admin.PostAsJsonAsync(U("/api/payroll/back-payments"), new
        {
            employeeId = abebe.Id, assignmentId = raise!.Id, effectiveFrom = "2026-06-08", effectiveTo = "2026-07-07", payrollDate = "2026-11-25",
            reason = "Raise entered after Sene was paid", reference = (string?)null,
        }, Ct);
        Assert.Equal(HttpStatusCode.Created, backPay.StatusCode);
        var arrears = (await backPay.Content.ReadFromJsonAsync<SalaryBackPaymentDto>(Ct))!;
        Assert.Equal((1_000m, 300m, 70m, 630m), (arrears.TotalGrossArrears, arrears.TotalAdditionalTax, arrears.TotalOtherDeductions, arrears.NetArrears));
        Assert.Equal("Sal Slip/HR-EMP-0001/00012", Assert.Single(arrears.Months).Source);

        // ERPNext worked the tax out differently for one slip (annual projection): the check names the slip and the component.
        var slip = erp.Slips[1];
        erp.Slips[1] = slip with { NetPay = 4_019m, Rows = [.. slip.Rows.Where(r => r.Abbreviation != "IT"), Row("s2", true, "Income Tax", "IT", null, 301m)] };
        var check = (await Run(erp, new MigrationOptions(name, Steps: ["payroll"]))).Single(r => r.Name == "payroll");
        Assert.False(check.Ok);
        Assert.Equal("Sal Slip/HR-EMP-0002/00001 (HR-EMP-0002): IT Gebeta 300.00, ERPNext 301.00; net pay Gebeta 4,020.00, ERPNext 4,019.00", Assert.Single(check.Failures));
    }

    [Fact]
    public async Task Annual_leave_balances_come_over_once_and_the_years_allocation_is_not_given_twice()
    {
        const string name = "Tana Beles Leave Test";
        var erp = new FakeErpNext(name, "0800000210");
        erp.Employees.AddRange(
        [
            new("HR-EMP-0001", "Abebe", "Kebede", "Tesfaye", "Abebe Kebede Tesfaye", "Male", new(2015, 1, 1), "Active", null, null),
            new("HR-EMP-0002", "Almaz", "Tadesse", null, "Almaz Tadesse", "Female", new(2024, 1, 1), "Active", null, null),
            new("HR-EMP-0003", "Hana", "Girma", null, "Hana Girma", "Female", new(2020, 1, 1), "Active", null, null),
        ]);
        erp.Leaves.AddRange(
        [
            new("HR-EMP-0001", "Annual Leave 2016", 12m, false),                 // carried from an earlier year
            new("HR-EMP-0001", "የአመት እረፍት 2018 (ሰራተኛ)", 8.5m, true),           // this year's, already allocated in ERPNext
            new("HR-EMP-0001", "Compensatory", 2m, false),                     // no balance in Gebeta
            new("HR-EMP-0002", "Annual Leave 2016", 0m, false),                // nothing to bring; Gebeta allocates the year
            new("HR-EMP-0003", "Annual", -1m, true),                           // more taken than allocated
        ]);
        var options = new MigrationOptions(name, Steps: ["employees", "leave"], Cutover: new DateOnly(2026, 7, 7)); // Sene 30, 2018

        var leave = (await Run(erp, options)).Single(r => r.Name == "leave");
        Assert.Equal(1, leave.Created);
        Assert.Equal("HR-EMP-0003: annual leave balance -1 days (more taken than allocated); correct it in ERPNext.", Assert.Single(leave.Failures));
        Assert.Contains(leave.Warnings, w => w.StartsWith("Compensatory: 1 employees hold 2 days", StringComparison.Ordinal));
        Assert.Contains("this run 1 of 1 balances equal ERPNext (20.5 days)", leave.Check, StringComparison.Ordinal);

        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = (await admin.GetFromJsonAsync<List<CompanyDto>>(U("/api/core/companies"), Ct))!.Single(c => c.Name == name);
        var employees = (await admin.GetFromJsonAsync<List<EmployeeDto>>(U($"/api/core/employees?companyId={company.Id}"), Ct))!;
        var abebe = employees.Single(e => e.FirstName == "Abebe");
        var balance = (await admin.GetFromJsonAsync<List<LeaveBalanceDto>>(U($"/api/hr/employees/{abebe.Id}/leave-balance"), Ct))!.Single(b => b.Type == "Annual");
        Assert.Equal(20.5m, balance.Balance);

        // Allocating 2018 now skips Abebe (his opening balance holds the year) and gives Almaz and Hana theirs.
        var allocation = await (await admin.PostAsJsonAsync(U("/api/hr/leave-allocations"), new { companyId = company.Id, fiscalYear = 2018 }, Ct))
            .Content.ReadFromJsonAsync<AllocationResultDto>(Ct);
        Assert.Equal((2, 1), (allocation!.Allocated, allocation.AlreadyAllocated));

        var again = (await Run(erp, options)).Single(r => r.Name == "leave");
        Assert.Equal((0, 1), (again.Created, again.AlreadyMigrated));
    }

    [Fact]
    public void An_open_order_line_comes_over_in_stock_units_with_lines_of_one_item_merged()
    {
        ErpOrderLine Line(decimal qty, decimal done, decimal rate, decimal factor, string warehouse, decimal billed = 0m) =>
            new("Purchase", "PUR-ORD-0001", "Supplier", new(2026, 10, 1), null, "PEN", warehouse, qty, done, rate, factor, billed);

        var (lines, warnings, notBilled) = MigrationRun.Remaining(
        [
            Line(10m, 4m, 120m, 12m, "Stores - X", billed: 300m), // 6 boxes of 12 left: 72 pens at 10
            Line(2m, 0m, 100m, 1m, "Shop - X"),                    // 2 pens at 100
        ]);

        var line = Assert.Single(lines);
        Assert.Equal((74m, 920m), (line.Qty, line.Amount));
        Assert.Equal("Stores - X", line.Warehouse);
        Assert.Contains("merged into Stores - X", Assert.Single(warnings), StringComparison.Ordinal);
        Assert.Equal(180m, notBilled); // 4 boxes received (480) less 300 billed
    }

    [Fact]
    public async Task Open_orders_come_over_for_what_is_left_and_a_supplier_becomes_both_when_its_customer_side_has_an_order()
    {
        const string name = "Gambella Orders Test";
        var erp = new FakeErpNext(name, "0800000220");
        erp.Customers.Add(new("Addis Cement Factory", "Addis Cement Factory", null, true)); // disabled customer side of an active supplier
        var cutover = new DateOnly(2026, 7, 7);

        // Masters first, without a cut-over: the factory comes over as a supplier only.
        await Run(erp, new MigrationOptions(name, Steps: ["parties", "items", "warehouses"]));

        erp.Orders.AddRange(
        [
            new("Purchase", "PUR-ORD-0007", "Addis Cement Factory", new(2026, 6, 20), new(2026, 7, 30), "Cement 50 kg", "Stores - GAM", 100m, 40m, 520m, 1m, 10_400m),
            new("Sales", "SAL-ORD-0003", "CUST-0001", new(2026, 6, 25), null, "SUGAR-WHITE", "Finished Goods - GAM", 10m, 10m, 3_000m, 1m, 0m), // only billing open
            new("Sales", "SAL-ORD-0004", "Addis Cement Factory", new(2026, 7, 1), new(2026, 7, 15), "SUGAR-WHITE", "Finished Goods - GAM", 5m, 0m, 3_100m, 1m, 0m),
        ]);
        var reports = await Run(erp, new MigrationOptions(name, Steps: ["parties", "orders"], Cutover: cutover));

        Assert.Contains(reports.Single(r => r.Name == "parties").Warnings, w => w == "Addis Cement Factory: was only a supplier in Gebeta; now customer and supplier.");
        var orders = reports.Single(r => r.Name == "orders");
        Assert.True(orders.Ok, string.Join("; ", orders.Failures));
        Assert.Equal("2 of 2 open orders (this run: 46,700.00 still to receive or deliver, each order's total equal)", orders.Check);
        Assert.Contains(orders.Warnings, w => w.StartsWith("1 orders are fully received or delivered", StringComparison.Ordinal));
        Assert.Contains(orders.Warnings, w => w.StartsWith("Received but not billed: 10,400.00; delivered but not billed: 30,000.00.", StringComparison.Ordinal));

        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = (await admin.GetFromJsonAsync<List<CompanyDto>>(U("/api/core/companies"), Ct))!.Single(c => c.Name == name);
        var purchase = Assert.Single((await admin.GetFromJsonAsync<List<PurchaseOrderDto>>(U($"/api/buying/purchase-orders?companyId={company.Id}&from=2026-01-01&to=2026-12-31"), Ct))!);
        Assert.Equal(("Submitted", 31_200m, "Open at cut-over 2026-07-07: PUR-ORD-0007"), (purchase.Status, purchase.NetTotal, purchase.Remark)); // 60 bags at 520
        Assert.Equal(60m, Assert.Single(purchase.Lines).Qty);
        var sales = Assert.Single((await admin.GetFromJsonAsync<List<SalesOrderDto>>(U($"/api/selling/sales-orders?companyId={company.Id}&from=2026-01-01&to=2026-12-31"), Ct))!);
        Assert.Equal(15_500m, sales.NetTotal);

        var again = (await Run(erp, new MigrationOptions(name, Steps: ["parties", "orders"], Cutover: cutover))).Single(r => r.Name == "orders");
        Assert.Equal((0, 2), (again.Created, again.AlreadyMigrated));
    }

    [Fact]
    public async Task Users_come_over_without_roles_until_an_administrator_chooses_them_in_the_roles_file()
    {
        const string name = "Arba Minch Users Test";
        var erp = new FakeErpNext(name, "0800000240");
        string[] everything = ["System Manager", "Accounts Manager", "HR Manager", "Stock User", "Blogger"]; // ERPNext's usual "give them all"
        erp.Users.AddRange(
        [
            new("meron@arba.et", "meron", "meron@arba.et", "Meron Alemu", everything, []),
            new("dawit@arba.et", null, "dawit@arba.et", "Dawit Haile", everything, [name]),
            new("other@x.et", "other", "other@x.et", "Other Company", ["Accounts User"], ["Some Other Company"]), // not this company
        ]);
        var dir = Directory.CreateTempSubdirectory("gebeta-users-");
        var roles = Path.Combine(dir.FullName, "roles.csv");
        var keycloak = Path.Combine(dir.FullName, "keycloak.json");
        var options = new MigrationOptions(name, Steps: ["users"], UserRolesPath: roles, KeycloakUsersPath: keycloak);
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);

        try
        {
            var first = (await Run(erp, options)).Single(r => r.Name == "users");
            Assert.Equal(2, first.Created);
            Assert.Contains(first.Warnings, w => w.StartsWith("2 users are System Managers in ERPNext.", StringComparison.Ordinal));
            var all = (await admin.GetFromJsonAsync<List<UserDto>>(U("/api/core/users"), Ct))!;
            var meron = all.Single(u => u.Username == "meron");
            Assert.Equal((true, 0), (meron.IsInvited, meron.Roles.Count)); // nothing until chosen
            Assert.Contains(all, u => u.Username == "dawit@arba.et");     // no ERPNext username: the email is the username

            // The review file suggests business roles, never System Manager; gebeta_roles starts empty.
            var lines = await File.ReadAllLinesAsync(roles, Ct);
            Assert.EndsWith(",Accountant; HR Officer; Payroll Officer; Store Keeper,", lines.Single(l => l.StartsWith("meron@arba.et", StringComparison.Ordinal)), StringComparison.Ordinal);
            Assert.Contains("\"username\": \"meron\"", await File.ReadAllTextAsync(keycloak, Ct), StringComparison.Ordinal);

            // The administrator makes Meron the administrator and Dawit an accountant; the next run gives exactly that.
            await File.WriteAllLinesAsync(roles, lines.Select(l => l.StartsWith("meron@", StringComparison.Ordinal) ? l + "System Manager"
                : l.StartsWith("dawit@", StringComparison.Ordinal) ? l + "Accountant" : l), Ct);
            var second = (await Run(erp, options)).Single(r => r.Name == "users");
            Assert.True(second.Ok, string.Join("; ", second.Failures));
            all = (await admin.GetFromJsonAsync<List<UserDto>>(U("/api/core/users"), Ct))!;
            Assert.Equal(["System Manager"], all.Single(u => u.Username == "meron").Roles);
            Assert.Equal(["Accountant"], all.Single(u => u.Username == "dawit@arba.et").Roles);
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task An_installation_with_only_hr_and_payroll_gets_only_their_data_and_the_rest_is_skipped()
    {
        const string name = "Hawassa HR Only Test";
        var erp = new FakeErpNext(name, "0800000250");
        erp.Employees.Add(new("HR-EMP-0001", "Abebe", "Kebede", null, "Abebe Kebede", "Male", new(2020, 1, 1), "Active", null, null));
        erp.Leaves.Add(new("HR-EMP-0001", "Annual Leave 2018", 10m, true));
        erp.Stock.Add(new("Cement 50 kg", "Stores - HAW", 10m, 5_000m, []));
        erp.Outstanding.Add(new("Receivable", "Customer", "CUST-0001", "Sales Invoice", "SINV-0009", 1_000m, new(2026, 6, 1), null));
        erp.Orders.Add(new("Purchase", "PUR-ORD-0009", "Addis Cement Factory", new(2026, 6, 20), null, "Cement 50 kg", "Stores - HAW", 10m, 0m, 500m, 1m, 0m));

        var reports = await Run(erp, new MigrationOptions(name, Cutover: new DateOnly(2026, 7, 7), Modules: ["hr", "payroll"]));

        string Check(string step) => reports.Single(r => r.Name == step).Check!;
        Assert.Equal("skipped: stock is not installed here", Check("warehouses"));
        Assert.Equal("skipped: stock is not installed here", Check("stock"));
        Assert.Equal("skipped: accounting is not installed here", Check("invoices"));
        Assert.Equal("skipped: buying or selling is not installed here", Check("orders"));
        Assert.All(reports, r => Assert.True(r.Ok, r.Name + ": " + string.Join("; ", r.Failures)));
        Assert.Equal(1, reports.Single(r => r.Name == "leave").Created);

        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = (await admin.GetFromJsonAsync<List<CompanyDto>>(U("/api/core/companies"), Ct))!.Single(c => c.Name == name);
        Assert.Equal(3, (await admin.GetFromJsonAsync<List<ItemDto>>(U($"/api/core/items?companyId={company.Id}"), Ct))!.Count); // items are Core's
        Assert.Empty((await admin.GetFromJsonAsync<List<WarehouseDto>>(U($"/api/stock/warehouses?companyId={company.Id}"), Ct))!); // nothing written into Stock
        var cement = (await admin.GetFromJsonAsync<List<ItemDto>>(U($"/api/core/items?companyId={company.Id}"), Ct))!.Single(i => i.Code == "CEMENT-50-KG");
        Assert.Equal("MovingAverage", (await admin.GetFromJsonAsync<ItemStockSettingDto>(U($"/api/stock/items/{cement.Id}/settings"), Ct))!.Method); // FIFO not set without Stock
    }

    [Theory]
    [InlineData("Annual Leave 2016", true)]
    [InlineData("anual", true)]
    [InlineData("የአመት እረፍት 2016  (ስራ መሪ)", true)]
    [InlineData("Compensatory", false)]
    [InlineData("Leave without pay", false)]
    public void Annual_leave_is_recognised_by_its_name(string leaveType, bool annual) => Assert.Equal(annual, MigrationRun.IsAnnualLeave(leaveType));

    [Fact]
    public void A_fifo_queue_becomes_layers_that_add_up_to_erpnexts_value_to_the_cent()
    {
        var layers = MigrationRun.Layers(new ErpStockBalance("X", "W", 3m, 10m, [new(1m, 3.333m), new(1m, 3.333m), new(1m, 3.333m)]));
        Assert.Equal([(1m, 3.33m), (1m, 3.33m), (1m, 3.34m)], layers);

        // A queue that does not match the quantity (ERPNext after negative stock) becomes one line at the average.
        Assert.Equal([(5m, 50m)], MigrationRun.Layers(new ErpStockBalance("X", "W", 5m, 50m, [new(2m, 10m), new(2m, 10m)])));
    }

    [Theory]
    [InlineData("P7%", "P7")]
    [InlineData("2", "C_2")]
    [InlineData("hdf;fwff", "hdf_fwff")]
    [InlineData("BS", "BS_2")]          // BS is taken by the structure
    [InlineData("Employer pension 11% of basic", "Employer_pension_11")]
    public void Abbreviations_gebeta_cannot_read_are_cleaned_without_clashing(string abbreviation, string expected) =>
        Assert.Equal(expected, Codes.Abbreviation(abbreviation, new HashSet<string>(["BS"], StringComparer.Ordinal)));

    [Theory]
    [InlineData("Stores - ADSF", 20, "STORES")]
    [InlineData("Work In Progress - ADSF", 20, "WORK-IN-PROGRESS")]
    [InlineData("Cement 50 kg", 40, "CEMENT-50-KG")]
    [InlineData("Raw sugar (bulk) / grade A", 40, "RAW-SUGAR-BULK-GRADE-A")]
    [InlineData("A very long warehouse name indeed", 20, "A-VERY-LONG-WAREHOUS")]
    public void Codes_are_normalised_the_same_way_every_run(string name, int max, string expected) =>
        Assert.Equal(expected, Codes.Normalise(name, max));
}
