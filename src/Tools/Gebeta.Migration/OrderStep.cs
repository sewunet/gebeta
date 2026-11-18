using Gebeta.Buying.Application;
using Gebeta.Buying.Contracts;
using Gebeta.SharedKernel;
using Gebeta.Selling.Application;
using Gebeta.Selling.Contracts;

namespace Gebeta.Migration;

/// <summary>
/// Purchase and sales orders still open at cut-over (docs/data-migration.md step 10). Each becomes a new Gebeta order, submitted,
/// for what is still to receive or deliver, in stock units, with the old order number in its remark. Gebeta tracks an order's
/// progress from its own receipts, deliveries and invoices, so what happened before the cut-over is not replayed; goods received or
/// delivered but not yet billed are reported (they are in the opening balances).
/// </summary>
public sealed partial class MigrationRun
{
    /// <summary>One remaining line in stock units; lines of the same item are merged (Gebeta keeps an item once per order).</summary>
    public sealed record RemainingLine(string Item, string? Warehouse, decimal Qty, decimal Rate)
    {
        public decimal Amount => Rounding.ToCurrency(Qty * Rate);
    }

    /// <summary>What is left of an order, merged per item; warnings name what the merge or the units changed.</summary>
    public static (List<RemainingLine> Lines, List<string> Warnings, decimal NotBilled) Remaining(IReadOnlyList<ErpOrderLine> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var warnings = new List<string>();
        var notBilled = order.Sum(l => Math.Max(0m, Rounding.ToCurrency(Math.Min(l.Done, l.Qty) * l.Rate) - l.BilledAmount));
        var lines = new List<RemainingLine>();
        foreach (var item in order.Where(l => l.Qty - l.Done > 0m).GroupBy(l => l.Item, StringComparer.Ordinal))
        {
            var qty = item.Sum(l => decimal.Round((l.Qty - l.Done) * l.ConversionFactor, 3));
            var value = item.Sum(l => (l.Qty - l.Done) * l.Rate);
            var warehouses = item.Select(l => l.Warehouse).Distinct().ToList();
            if (warehouses.Count > 1)
            {
                warnings.Add($"{order[0].Order}, {item.Key}: lines for several warehouses are merged into {warehouses[0]}.");
            }

            lines.Add(new RemainingLine(item.Key, warehouses[0], qty, qty == 0m ? 0m : value / qty)); // full precision: qty × rate gives the value back
        }

        return (lines, warnings, notBilled);
    }

    private async Task OpenOrdersAsync(DateOnly cutover, StepReport report, CancellationToken cancellationToken)
    {
        var rows = await source.OpenOrderLinesAsync(options.Company, cutover, cancellationToken).ConfigureAwait(false);
        var party = await ResolverAsync("Party", cancellationToken).ConfigureAwait(false);
        var item = await ResolverAsync("Item", cancellationToken).ConfigureAwait(false);
        var warehouse = await ResolverAsync("Warehouse", cancellationToken).ConfigureAwait(false);
        decimal notBilledPurchase = 0m, notBilledSales = 0m, openValue = 0m;
        var onlyBilling = 0;
        var expected = 0;

        foreach (var order in rows.GroupBy(r => (r.Kind, r.Order)))
        {
            var lines = order.ToList();
            var (remaining, warnings, notBilled) = Remaining(lines);
            report.Warnings.AddRange(warnings);
            if (order.Key.Kind == "Purchase")
            {
                notBilledPurchase += notBilled;
            }
            else
            {
                notBilledSales += notBilled;
            }

            if (remaining.Count == 0)
            {
                onlyBilling++; // everything received or delivered; only billing is open
                continue;
            }

            expected++;
            var doctype = order.Key.Kind == "Purchase" ? "Purchase Order" : "Sales Order";
            var known = await KnownAsync(doctype, cancellationToken).ConfigureAwait(false);
            if (known.ContainsKey(order.Key.Order))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var first = lines[0];
            if (party(first.Party) is not { } partyId)
            {
                report.Failures.Add($"{order.Key.Order}: {(order.Key.Kind == "Purchase" ? "supplier" : "customer")} {first.Party} is not migrated.");
                continue;
            }

            var missing = remaining.Where(l => item(l.Item) is null).Select(l => l.Item).ToList();
            if (missing.Count > 0)
            {
                report.Failures.Add($"{order.Key.Order}: items not migrated: {string.Join(", ", missing)}.");
                continue;
            }

            var value = remaining.Sum(l => l.Amount);
            if (options.DryRun)
            {
                report.Created++;
                openValue += value;
                continue;
            }

            var dueDate = first.DueDate is { } due && due >= first.OrderDate ? due : (DateOnly?)null;
            var remark = $"Open at cut-over {cutover:yyyy-MM-dd}: {order.Key.Order}";
            Result<Guid> created = order.Key.Kind == "Purchase"
                ? await CreatePurchaseOrderAsync(partyId, first.OrderDate, dueDate, remaining, item, warehouse, remark, value, cancellationToken).ConfigureAwait(false)
                : await CreateSalesOrderAsync(partyId, first.OrderDate, dueDate, remaining, item, warehouse, remark, value, cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"{order.Key.Order}: {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, doctype, order.Key.Order, created.Value, cancellationToken).ConfigureAwait(false);
            report.Created++;
            openValue += value;
        }

        if (onlyBilling > 0)
        {
            report.Warnings.Add($"{onlyBilling} orders are fully received or delivered and only wait for billing; they are not brought (bill them directly in Gebeta).");
        }

        if (notBilledPurchase + notBilledSales > 0m)
        {
            report.Warnings.Add($"Received but not billed: {notBilledPurchase:N2}; delivered but not billed: {notBilledSales:N2}. They are in the opening balances; " +
                "enter the invoices in Gebeta without an order.");
        }

        var done = report.Created + report.AlreadyMigrated;
        report.CheckPassed = done == expected;
        report.Check = $"{(options.DryRun ? "dry run: " : "")}{done} of {expected} open orders" + (report.Created > 0 ? $" (this run: {openValue:N2} still to receive or deliver, each order's total equal)" : "");
    }

    // The order's total in Gebeta must be what was left in ERPNext, to the cent; otherwise the order is cancelled again and reported.
    private async Task<Result<Guid>> CreatePurchaseOrderAsync(Guid supplierId, DateOnly date, DateOnly? due, List<RemainingLine> lines, Func<string, Guid?> item,
        Func<string, Guid?> warehouse, string remark, decimal value, CancellationToken cancellationToken)
    {
        var draft = await Handle<CreatePurchaseOrderCommand, PurchaseOrderDto>(new CreatePurchaseOrderCommand(_companyId, supplierId, date, due,
            lines.Select(l => new PurchaseOrderLineInput(item(l.Item)!.Value, l.Qty, l.Rate, l.Warehouse is null ? null : warehouse(l.Warehouse))).ToList(), remark), cancellationToken)
            .ConfigureAwait(false);
        if (draft.IsFailure)
        {
            return draft.Error!;
        }

        if (draft.Value.NetTotal != value)
        {
            return Error.Validation($"the order would total {draft.Value.NetTotal:N2} in Gebeta, not {value:N2}; left as draft {draft.Value.Id} to check.");
        }

        var submitted = await Handle<PurchaseOrderActionCommand, PurchaseOrderDto>(new PurchaseOrderActionCommand(draft.Value.Id, PurchaseOrderAction.Submit), cancellationToken)
            .ConfigureAwait(false);
        return submitted.IsFailure ? submitted.Error! : draft.Value.Id;
    }

    private async Task<Result<Guid>> CreateSalesOrderAsync(Guid customerId, DateOnly date, DateOnly? due, List<RemainingLine> lines, Func<string, Guid?> item,
        Func<string, Guid?> warehouse, string remark, decimal value, CancellationToken cancellationToken)
    {
        var draft = await Handle<CreateSalesOrderCommand, SalesOrderDto>(new CreateSalesOrderCommand(_companyId, customerId, date, due,
            lines.Select(l => new SalesOrderLineInput(item(l.Item)!.Value, l.Qty, l.Rate, l.Warehouse is null ? null : warehouse(l.Warehouse))).ToList(), remark), cancellationToken)
            .ConfigureAwait(false);
        if (draft.IsFailure)
        {
            return draft.Error!;
        }

        if (draft.Value.NetTotal != value)
        {
            return Error.Validation($"the order would total {draft.Value.NetTotal:N2} in Gebeta, not {value:N2}; left as draft {draft.Value.Id} to check.");
        }

        var submitted = await Handle<SalesOrderActionCommand, SalesOrderDto>(new SalesOrderActionCommand(draft.Value.Id, SalesOrderAction.Submit), cancellationToken)
            .ConfigureAwait(false);
        return submitted.IsFailure ? submitted.Error! : draft.Value.Id;
    }
}
