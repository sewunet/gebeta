using System.Globalization;
using Gebeta.Accounting.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.SharedKernel;
using Gebeta.Stock.Application;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Domain.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Migration;

/// <summary>
/// Balances at the end of the cut-over day (docs/data-migration.md steps 7–9). Opening invoices carry no GL; the opening
/// trial balance brings the receivable and payable accounts. Opening stock is received against 3900 Opening Balance
/// Clearing, and the trial balance posts ERPNext's stock accounts to 3900 as well, so 3900 ends at zero exactly when stock
/// value and stock accounts agree. The opening step's check compares Gebeta's trial balance with ERPNext's, account by account.
/// </summary>
public sealed partial class MigrationRun
{
    private const string OpeningInvoice = "Opening Invoice";
    private const string OpeningStock = "Opening Stock";
    private const string OpeningJournal = "Opening Balances";

    /// <summary>Invoices still open at cut-over, each as a Gebeta opening invoice for its outstanding amount, dated and due as in ERPNext.</summary>
    private async Task OpenInvoicesAsync(DateOnly cutover, StepReport report, CancellationToken cancellationToken)
    {
        var rows = await source.OutstandingAsync(options.Company, cutover, cancellationToken).ConfigureAwait(false);
        var party = await ResolverAsync("Party", cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync(OpeningInvoice, cancellationToken).ConfigureAwait(false);
        var open = rows.Where(r => r.Amount > 0m && r.PartyType is "Customer" or "Supplier").ToList();
        decimal receivable = 0m, payable = 0m;

        foreach (var row in rows.Except(open).Where(r => r.Amount > 0m))
        {
            report.Warnings.Add($"{row.VoucherNo}: {row.Amount:N2} open for {row.PartyType} {row.Party} is not a customer or supplier balance; bring it in by hand.");
        }

        foreach (var group in rows.Where(r => r.Amount < 0m).GroupBy(r => r.AccountType))
        {
            report.Warnings.Add($"{group.Count()} unallocated {(group.Key == "Receivable" ? "customer" : "supplier")} advances total {-group.Sum(r => r.Amount):N2}. " +
                "They stay in the control account through the opening balances; allocate them in Gebeta when the invoices come.");
        }

        foreach (var row in open)
        {
            var key = $"{row.VoucherType}/{row.VoucherNo}/{row.Party}";
            if (known.ContainsKey(key))
            {
                report.AlreadyMigrated++;
                continue;
            }

            if (party(row.Party) is not { } partyId)
            {
                report.Failures.Add($"{row.VoucherNo}: {row.PartyType} {row.Party} is not migrated (disabled in ERPNext?); {row.Amount:N2} is still open.");
                continue;
            }

            var amount = Rounding.ToCurrency(row.Amount);
            var kind = row.AccountType == "Receivable" ? InvoiceKind.Sales : InvoiceKind.Purchase;
            if (options.DryRun)
            {
                report.Created++;
            }
            else
            {
                var created = await Handle<CreateOpeningInvoiceCommand, InvoiceDto>(
                    new CreateOpeningInvoiceCommand(kind, _companyId, partyId, row.PostingDate ?? cutover, row.DueDate, amount, row.VoucherNo), cancellationToken).ConfigureAwait(false);
                if (created.IsFailure)
                {
                    report.Failures.Add($"{row.VoucherNo}: {created.Error!.Message}");
                    continue;
                }

                await idMap.AddAsync(options.Company, OpeningInvoice, key, created.Value.Id, cancellationToken).ConfigureAwait(false);
                report.Created++;
            }

            if (kind == InvoiceKind.Sales)
            {
                receivable += amount;
            }
            else
            {
                payable += amount;
            }
        }

        var done = report.Created + report.AlreadyMigrated;
        report.CheckPassed = done == open.Count;
        report.Check = $"{(options.DryRun ? "dry run: " : "")}{done} of {open.Count} open invoices" +
            (report.Created > 0 ? $" (this run: receivable {receivable:N2}, payable {payable:N2})" : "");
    }

    /// <summary>
    /// One receipt per warehouse on the cut-over date, against 3900. FIFO items keep ERPNext's queue as separate lines so their
    /// layers carry on; the value of each warehouse is ERPNext's to the cent.
    /// </summary>
    private async Task OpeningStockAsync(DateOnly cutover, StepReport report, CancellationToken cancellationToken)
    {
        var rows = await source.StockBalancesAsync(options.Company, cutover, cancellationToken).ConfigureAwait(false);
        var item = await ResolverAsync("Item", cancellationToken).ConfigureAwait(false);
        var warehouse = await ResolverAsync("Warehouse", cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync(OpeningStock, cancellationToken).ConfigureAwait(false);
        var expected = new Dictionary<Guid, (string Name, decimal Value)>();

        foreach (var group in rows.GroupBy(r => r.Warehouse, StringComparer.Ordinal))
        {
            var value = Rounding.ToCurrency(group.Sum(r => r.Value));
            if (warehouse(group.Key) is not { } warehouseId)
            {
                report.Failures.Add($"{group.Key}: holds stock worth {value:N2} but is not migrated (disabled in ERPNext?).");
                continue;
            }

            expected[warehouseId] = (group.Key, value);
            if (known.ContainsKey(group.Key))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var lines = new List<StockEntryLineInput>();
            var problems = new List<string>();
            foreach (var row in group)
            {
                if (row.Qty < 0m)
                {
                    problems.Add($"{row.Item} in {group.Key}: negative stock {row.Qty} in ERPNext; correct it there first.");
                }
                else if (item(row.Item) is not { } itemId)
                {
                    problems.Add($"{row.Item} in {group.Key}: holds {row.Qty} but the item is not migrated (disabled in ERPNext?).");
                }
                else
                {
                    lines.AddRange(Layers(row).Select(l => new StockEntryLineInput(itemId, l.Qty, l.Value / l.Qty, null, warehouseId)));
                }
            }

            if (problems.Count > 0)
            {
                report.Failures.AddRange(problems); // the whole warehouse waits: half a warehouse would fail the check anyway
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<CreateStockEntryCommand, StockEntryDto>(new CreateStockEntryCommand(_companyId, StockEntryPurpose.Receipt, cutover, lines,
                EthiopianChartOfAccounts.OpeningClearing, $"Opening stock at cut-over {cutover:yyyy-MM-dd}", null, group.Key), cancellationToken).ConfigureAwait(false);
            var submitted = created.IsFailure ? created : await Handle<SubmitStockEntryCommand, StockEntryDto>(new SubmitStockEntryCommand(created.Value.Id), cancellationToken).ConfigureAwait(false);
            if (submitted.IsFailure)
            {
                report.Failures.Add($"{group.Key}: {submitted.Error!.Message}{(created.IsSuccess ? $" (draft {created.Value.Id} left for review)" : "")}");
                continue;
            }

            await idMap.AddAsync(options.Company, OpeningStock, group.Key, submitted.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        var total = expected.Values.Sum(e => e.Value);
        if (options.DryRun || _companyId == Guid.Empty)
        {
            report.Check = $"dry run: {report.Created} of {expected.Count} warehouses, stock value {total:N2}";
            report.CheckPassed = report.Failures.Count == 0;
            return;
        }

        var balance = (await Query<StockBalanceQuery, StockBalanceDto>(new StockBalanceQuery(_companyId, cutover), cancellationToken).ConfigureAwait(false)).Value;
        var actual = balance.Rows.GroupBy(r => r.WarehouseId).ToDictionary(g => g.Key, g => g.Sum(r => r.Value));
        var differences = expected.Where(e => actual.GetValueOrDefault(e.Key) != e.Value.Value)
            .Select(e => $"{e.Value.Name}: Gebeta {actual.GetValueOrDefault(e.Key):N2}, ERPNext {e.Value.Value:N2}").ToList();
        report.Failures.AddRange(differences);
        report.CheckPassed = differences.Count == 0;
        report.Check = $"stock value per warehouse at {cutover:yyyy-MM-dd}: {expected.Count - differences.Count} of {expected.Count} equal ERPNext (total {total:N2})";
    }

    /// <summary>ERPNext's FIFO queue as receipt lines whose values add up to the stock value exactly; one line when the queue does not add up.</summary>
    public static IReadOnlyList<(decimal Qty, decimal Value)> Layers(ErpStockBalance row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var value = Rounding.ToCurrency(row.Value);
        var queue = row.Queue.Where(l => l.Qty != 0m).ToList();
        if (queue.Count < 2 || queue.Any(l => l.Qty < 0m || l.Rate < 0m) || queue.Sum(l => l.Qty) != row.Qty)
        {
            return [(row.Qty, value)];
        }

        var layers = queue.Take(queue.Count - 1).Select(l => (l.Qty, Value: Rounding.ToCurrency(l.Qty * l.Rate))).ToList();
        var rest = value - layers.Sum(l => l.Value);
        if (rest < 0m)
        {
            return [(row.Qty, value)];
        }

        layers.Add((queue[^1].Qty, rest)); // the last layer takes the rounding, so the warehouse total is ERPNext's
        return layers;
    }

    /// <summary>
    /// ERPNext's trial balance at cut-over as one opening journal. Balance-sheet accounts bring their whole balance, income and
    /// expense accounts only this fiscal year's (earlier years' profit or loss goes to retained earnings), stock accounts go to
    /// 3900 to meet the opening stock.
    /// </summary>
    private async Task OpeningBalancesAsync(DateOnly cutover, StepReport report, CancellationToken cancellationToken)
    {
        var ethiopian = true; // a company the migration creates gets Gebeta's default, the Ethiopian fiscal year
        if (_companyId != Guid.Empty)
        {
            await using var scope = services.CreateAsyncScope();
            ethiopian = (await scope.ServiceProvider.GetRequiredService<ICompanyDirectory>().GetAsync(_companyId, cancellationToken).ConfigureAwait(false))?.UsesEthiopianFiscalYear ?? true;
        }

        var year = FiscalYearSpan.Containing(cutover, ethiopian);
        var balances = (await source.TrialBalanceAsync(options.Company, cutover, year.Start, cancellationToken).ConfigureAwait(false))
            .Where(b => b.Balance != 0m || b.BalanceBeforeYear != 0m).ToList();

        var codes = EthiopianChartOfAccounts.Accounts.Where(a => !a.IsGroup).Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
        if (_companyId != Guid.Empty)
        {
            var accounts = (await Query<ListAccountsQuery, IReadOnlyList<AccountDto>>(new ListAccountsQuery(_companyId), cancellationToken).ConfigureAwait(false)).Value;
            codes.UnionWith(accounts.Where(a => !a.IsGroup).Select(a => a.Code));
            codes.ExceptWith(accounts.Where(a => a.IsGroup).Select(a => a.Code));
        }

        if (options.AccountMapPath is not { } path)
        {
            report.Failures.Add("Give --account-map <file.csv>: which Gebeta account each ERPNext account goes to. A missing file is written with suggestions.");
            return;
        }

        if (!File.Exists(path))
        {
            AccountMap.Write(path, balances, b => AccountMap.Suggest(b, codes));
            report.Failures.Add($"Wrote {path} with suggested Gebeta accounts for {balances.Count} ERPNext accounts. Review it (blank codes must be filled), then run again.");
            return;
        }

        var map = AccountMap.Read(path);
        var expected = new Dictionary<string, decimal>(StringComparer.Ordinal); // where each balance belongs in Gebeta's books
        var posted = new Dictionary<string, decimal>(StringComparer.Ordinal);   // what the journal posts (stock via 3900)
        foreach (var b in balances)
        {
            if (map.GetValueOrDefault(b.Account) is not { } code)
            {
                report.Failures.Add($"{b.Account} ({b.RootType}, balance {b.Balance:N2}): no Gebeta account in {path}.");
                continue;
            }

            if (!codes.Contains(code))
            {
                report.Failures.Add($"{b.Account}: {code} is not a postable Gebeta account (add it to the chart first, or pick another).");
                continue;
            }

            var amount = Rounding.ToCurrency(b.RootType is "Income" or "Expense" ? b.Balance - b.BalanceBeforeYear : b.Balance);
            Add(expected, code, amount);
            Add(posted, b.AccountType == "Stock" ? EthiopianChartOfAccounts.OpeningClearing : code, amount);
        }

        if (report.Failures.Count > 0)
        {
            return;
        }

        // Every ERPNext GL balances, so what is left is earlier years' profit or loss (plus each account's rounding to the cent).
        var rounded = posted;
        var earlier = -rounded.Values.Sum();
        if (earlier != 0m)
        {
            Add(rounded, EthiopianChartOfAccounts.RetainedEarnings, earlier);
            Add(expected, EthiopianChartOfAccounts.RetainedEarnings, earlier);
            report.Warnings.Add($"Profit and loss of the years before {year.Label}, {(earlier > 0m ? "a loss of " : "a profit of ")}{Math.Abs(earlier):N2}, goes to retained earnings (3200).");
        }

        var lines = rounded.Where(r => r.Value != 0m).OrderBy(r => r.Key, StringComparer.Ordinal)
            .Select(r => new JournalLineInput(r.Key, Math.Max(r.Value, 0m), Math.Max(-r.Value, 0m), "Opening balance")).ToList();

        var known = await KnownAsync(OpeningJournal, cancellationToken).ConfigureAwait(false);
        if (known.ContainsKey("GL"))
        {
            report.AlreadyMigrated++;
        }
        else if (options.DryRun)
        {
            report.Created++;
            report.Check = $"dry run: a balanced journal of {lines.Count} lines, debits {lines.Sum(l => l.Debit):N2}";
            return;
        }
        else
        {
            var journal = await Handle<PostOpeningBalancesCommand, JournalEntryDto>(
                new PostOpeningBalancesCommand(_companyId, cutover, lines, $"Opening balances at cut-over {cutover:yyyy-MM-dd}"), cancellationToken).ConfigureAwait(false);
            if (journal.IsFailure)
            {
                report.Failures.Add(journal.Error!.Message);
                return;
            }

            await idMap.AddAsync(options.Company, OpeningJournal, "GL", journal.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        // The proof: Gebeta's trial balance on the cut-over day is ERPNext's, account by account. Stock shows up here when the
        // opening stock and the stock accounts disagree (1300 short or over, 3900 not zero). The opening stock's journals reach
        // the ledger through the running Gebeta server (outbox → RabbitMQ → Accounting), so give them a moment to arrive.
        var differences = await TrialBalanceDifferencesAsync(cutover, expected, cancellationToken).ConfigureAwait(false);
        for (var waited = TimeSpan.Zero; differences.Count > 0 && waited < options.SettleTimeout; waited += SettlePoll)
        {
            await Task.Delay(SettlePoll, cancellationToken).ConfigureAwait(false);
            differences = await TrialBalanceDifferencesAsync(cutover, expected, cancellationToken).ConfigureAwait(false);
        }

        report.Failures.AddRange(differences);
        report.CheckPassed = differences.Count == 0;
        report.Check = $"trial balance at {cutover:yyyy-MM-dd}: " + (differences.Count == 0 ? "every account equals ERPNext"
            : $"{differences.Count} accounts differ. Opening stock journals are posted by the running Gebeta server; if it was not running, start it and run --steps opening again to re-check.");
    }

    private static readonly TimeSpan SettlePoll = TimeSpan.FromSeconds(2);

    private async Task<List<string>> TrialBalanceDifferencesAsync(DateOnly cutover, Dictionary<string, decimal> expected, CancellationToken cancellationToken)
    {
        var tb = (await Query<TrialBalanceQuery, TrialBalanceDto>(new TrialBalanceQuery(_companyId, cutover), cancellationToken).ConfigureAwait(false)).Value;
        var actual = tb.Rows.ToDictionary(r => r.Code, r => r.Debit - r.Credit, StringComparer.Ordinal);
        return expected.Keys.Union(actual.Keys).Append(EthiopianChartOfAccounts.OpeningClearing).Distinct()
            .Select(code => (code, want: expected.GetValueOrDefault(code), have: actual.GetValueOrDefault(code)))
            .Where(d => d.want != d.have)
            .OrderBy(d => d.code, StringComparer.Ordinal)
            .Select(d => $"{d.code}: Gebeta {d.have.ToString("N2", CultureInfo.InvariantCulture)}, ERPNext {d.want.ToString("N2", CultureInfo.InvariantCulture)}")
            .ToList();
    }

    private static void Add(Dictionary<string, decimal> totals, string code, decimal amount) => totals[code] = totals.GetValueOrDefault(code) + amount;
}
