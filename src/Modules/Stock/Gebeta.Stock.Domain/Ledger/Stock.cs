using Gebeta.SharedKernel;

namespace Gebeta.Stock.Domain.Ledger;

/// <summary>A place stock is kept. Its stock value is carried on <see cref="StockAccountCode"/> (perpetual inventory).</summary>
public sealed class Warehouse : Entity
{
    public const string DefaultStockAccount = "1300";

    private Warehouse()
    {
        Code = string.Empty;
        Name = string.Empty;
        StockAccountCode = DefaultStockAccount;
    }

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string StockAccountCode { get; private set; }

    public static Result<Warehouse> Create(Guid companyId, string code, string name, string? stockAccountCode)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 1 or > 20 || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("A warehouse needs a code (at most 20 characters) and a name.");
        }

        return new Warehouse
        {
            CompanyId = companyId, Code = code, Name = name.Trim(),
            StockAccountCode = string.IsNullOrWhiteSpace(stockAccountCode) ? DefaultStockAccount : stockAccountCode.Trim(),
        };
    }
}

/// <summary>Stock's own settings for a Core item: the valuation method.</summary>
public sealed class StockItemSetting : Entity
{
    private StockItemSetting()
    {
    }

    public Guid CompanyId { get; private set; }
    public Guid ItemId { get; private set; }
    public ValuationMethod Method { get; private set; }

    public static StockItemSetting Create(Guid companyId, Guid itemId, ValuationMethod method) => new() { CompanyId = companyId, ItemId = itemId, Method = method };

    public void Change(ValuationMethod method, bool hasMovements)
    {
        if (hasMovements && method != Method)
        {
            throw new DomainException("stock.method_fixed", "The valuation method cannot change once the item has stock movements.");
        }

        Method = method;
    }
}

public enum StockEntryPurpose
{
    Receipt,
    Issue,
    Transfer,

    /// <summary>A stock count: sets each line's quantity (and rate) in its warehouse; the difference is booked against the counter account.</summary>
    Reconciliation,
}

/// <summary>A line of a stock entry. <see cref="Rate"/> is given only for receipts; issues and transfers take it from the valuation engine.</summary>
public sealed record StockEntryLine(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Qty, decimal? Rate, Guid? SourceWarehouseId, Guid? TargetWarehouseId,
    decimal Value = 0m);

/// <summary>
/// Moves stock in (receipt), out (issue) or between warehouses (transfer). Submitting writes the stock ledger and asks
/// Accounting for the value journal; cancelling (only while it is the latest movement of its items) undoes both.
/// </summary>
public sealed class StockEntry : DocumentBase
{
    public const string DefaultCounterAccount = "5100";

    /// <summary>Goods received from a supplier, before or without the supplier's invoice; the invoice debits it back.</summary>
    public const string StockReceivedNotBilled = "2150";

    /// <summary>Goods delivered to a customer.</summary>
    public const string CostOfSales = "5000";

    private List<StockEntryLine> _lines = [];

    private StockEntry()
    {
        CounterAccountCode = DefaultCounterAccount;
    }

    private StockEntry(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        CounterAccountCode = DefaultCounterAccount;
    }

    public override string DocumentType => "Stock Entry";

    public StockEntryPurpose Purpose { get; private set; }
    public string? Remark { get; private set; }

    /// <summary>The supplier the goods came from (receipt) or the customer they went to (issue), if any.</summary>
    public Guid? PartyId { get; private set; }

    public string? PartyName { get; private set; }

    /// <summary>The supplier's delivery or invoice number, or our sales invoice number.</summary>
    public string? Reference { get; private set; }

    /// <summary>The other side of the value journal for receipts and issues (default 5100 Stock Adjustment).</summary>
    public string CounterAccountCode { get; private set; }

    public IReadOnlyList<StockEntryLine> Lines => _lines;
    public decimal TotalValue => _lines.Sum(l => l.Value);

    /// <param name="party">A supplier for goods received or a customer for goods delivered. The counter account then defaults to
    /// 2150 Stock Received But Not Billed (receipts) or 5000 Cost of Sales (issues) instead of 5100 Stock Adjustment.</param>
    public static StockEntry Create(Guid companyId, StockEntryPurpose purpose, DateOnly postingDate, IReadOnlyList<StockEntryLine> lines, string? counterAccountCode, string? remark,
        (Guid Id, string Name)? party = null, string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (party is not null && purpose == StockEntryPurpose.Transfer)
        {
            throw new DomainException("stock.entry_party", "A transfer between our own warehouses has no supplier or customer.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("stock.entry_lines", "A stock entry needs at least one line.");
        }

        foreach (var l in lines)
        {
            if ((purpose == StockEntryPurpose.Reconciliation ? l.Qty < 0m : l.Qty <= 0m) || decimal.Round(l.Qty, 3) != l.Qty)
            {
                throw new DomainException("stock.entry_qty", purpose == StockEntryPurpose.Reconciliation
                    ? $"{l.ItemCode}: the counted quantity cannot be negative and has at most three decimals."
                    : $"{l.ItemCode}: the quantity must be positive, with at most three decimals.");
            }

            var ok = purpose switch
            {
                StockEntryPurpose.Reconciliation => l.TargetWarehouseId is not null && l.SourceWarehouseId is null && l.Rate is null or >= 0m,
                StockEntryPurpose.Receipt => l.TargetWarehouseId is not null && l.SourceWarehouseId is null && l.Rate is >= 0m,
                StockEntryPurpose.Issue => l.SourceWarehouseId is not null && l.TargetWarehouseId is null,
                _ => l.SourceWarehouseId is not null && l.TargetWarehouseId is not null && l.SourceWarehouseId != l.TargetWarehouseId,
            };
            if (!ok)
            {
                throw new DomainException("stock.entry_warehouse", purpose switch
                {
                    StockEntryPurpose.Reconciliation => $"{l.ItemCode}: a stock count needs the warehouse counted.",
                    StockEntryPurpose.Receipt => $"{l.ItemCode}: a receipt needs a target warehouse and a rate.",
                    StockEntryPurpose.Issue => $"{l.ItemCode}: an issue needs a source warehouse.",
                    _ => $"{l.ItemCode}: a transfer needs two different warehouses.",
                });
            }
        }

        if (purpose == StockEntryPurpose.Reconciliation && lines.GroupBy(l => (l.ItemId, l.TargetWarehouseId)).Any(g => g.Count() > 1))
        {
            throw new DomainException("stock.count_duplicate", "Each item is counted once per warehouse.");
        }

        if (party is not null && purpose == StockEntryPurpose.Reconciliation)
        {
            throw new DomainException("stock.entry_party", "A stock count has no supplier or customer.");
        }

        var defaultCounter = party is null ? DefaultCounterAccount : purpose == StockEntryPurpose.Receipt ? StockReceivedNotBilled : CostOfSales;
        return new StockEntry(companyId, postingDate)
        {
            Purpose = purpose, _lines = lines.Select(l => l with { Value = 0m }).ToList(),
            CounterAccountCode = string.IsNullOrWhiteSpace(counterAccountCode) ? defaultCounter : counterAccountCode.Trim(),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
            PartyId = party?.Id, PartyName = party?.Name,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
        };
    }

    internal void SetLineValues(IReadOnlyList<decimal> values) => _lines = _lines.Select((l, i) => l with { Value = values[i] }).ToList();

    /// <summary>Reposting changed the value of one line (issues and transfers; a receipt's value never changes).</summary>
    public void RevalueLine(int index, decimal value) => _lines = _lines.Select((l, i) => i == index ? l with { Value = value } : l).ToList();
}

/// <summary>What a ledger row is: its value is fixed (receipt), taken from its transfer-out row (transfer in), or computed by the engine (issue, transfer out).</summary>
public enum MovementKind
{
    Receipt,
    Issue,
    TransferOut,
    TransferIn,

    /// <summary>A stock count: the quantity and value after it are fixed; its quantity and value change follow from what was there before.</summary>
    Reconciliation,
}

/// <summary>
/// A row of the stock ledger: one movement of one item in one warehouse, with the state after it. The movement itself
/// never changes; the computed columns (value change, quantity and value after, FIFO queue) are rewritten by reposting
/// when an earlier movement is posted or cancelled. Rows are ordered by posting date, then sequence.
/// </summary>
public sealed class StockLedgerEntry : Entity
{
    private List<FifoLayer> _queue = [];

    private StockLedgerEntry()
    {
        VoucherNumber = string.Empty;
    }

    /// <summary>Posting order (database sequence); later movements have higher numbers.</summary>
    public long Sequence { get; private set; }

    /// <summary>
    /// Gives a new row its place in the posting order. Done by persistence just before saving, in the order the rows were added: the
    /// order matters (the last row of an item and warehouse is its stock), and the database would otherwise number rows in the order
    /// they happen to be sent.
    /// </summary>
    public void AssignSequence(long sequence)
    {
        if (Sequence != 0)
        {
            throw new DomainException("stock.sequence_assigned", "This stock ledger row already has its place in the posting order.");
        }

        Sequence = sequence;
    }

    public Guid CompanyId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public decimal ActualQty { get; private set; }
    public decimal ValueChange { get; private set; }
    public decimal QtyAfter { get; private set; }
    public decimal ValueAfter { get; private set; }
    public ValuationMethod Method { get; private set; }
    public MovementKind Kind { get; private set; }
    public IReadOnlyList<FifoLayer> Queue => _queue;
    public Guid VoucherId { get; private set; }
    public string VoucherNumber { get; private set; }
    public int LineIndex { get; private set; }
    public bool IsCancelled { get; private set; }

    /// <summary>The rate of this movement (value change per unit).</summary>
    public decimal Rate => ActualQty == 0m ? 0m : ValueChange / ActualQty;

    public ValuationState State => new(QtyAfter, ValueAfter, _queue);

    internal static StockLedgerEntry Create(StockEntry entry, int lineIndex, Guid itemId, Guid warehouseId, decimal qty, decimal valueChange, ValuationState after,
        ValuationMethod method, MovementKind kind) => new()
    {
        Kind = kind,
        CompanyId = entry.CompanyId, ItemId = itemId, WarehouseId = warehouseId, PostingDate = entry.PostingDate, ActualQty = qty, ValueChange = valueChange,
        QtyAfter = after.Qty, ValueAfter = after.Value, Method = method, _queue = after.Queue.ToList(), VoucherId = entry.Id, VoucherNumber = entry.Name ?? string.Empty,
        LineIndex = lineIndex,
    };

    public void MarkCancelled() => IsCancelled = true;

    /// <summary>Reposting: the new computed value of this movement and the state after it.</summary>
    /// <summary>Reposting a stock count: the counted state stays; what changes is the difference from the stock before it.</summary>
    internal bool Recount(ValuationState before)
    {
        var qty = QtyAfter - before.Qty;
        var change = ValueAfter - before.Value;
        var changed = qty != ActualQty || change != ValueChange;
        (ActualQty, ValueChange) = (qty, change);
        return changed;
    }

    internal bool Revalue(decimal valueChange, ValuationState after)
    {
        var changed = valueChange != ValueChange || after.Qty != QtyAfter || after.Value != ValueAfter || !after.Queue.SequenceEqual(_queue);
        (ValueChange, QtyAfter, ValueAfter, _queue) = (valueChange, after.Qty, after.Value, after.Queue.ToList());
        return changed;
    }
}

/// <summary>
/// Reposting (docs/modules/stock.md): replays the ledger rows of one item and warehouse from a starting state, in posting
/// order. Receipts keep their value, transfers in take the (possibly new) value of their transfer out, issues and
/// transfers out get their value from the engine again. A replay that would take stock below zero at any point fails.
/// </summary>
public static class Reposting
{
    /// <returns>The rows whose computed values changed, with their value change before reposting.</returns>
    public static IReadOnlyList<(StockLedgerEntry Row, decimal OldValueChange)> Replay(ValuationState start, IReadOnlyList<StockLedgerEntry> rows, ValuationMethod method,
        Func<StockLedgerEntry, decimal?> transferInValue)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(transferInValue);
        var state = start;
        var changed = new List<(StockLedgerEntry, decimal)>();
        foreach (var row in rows)
        {
            var old = row.ValueChange;
            if (row.Kind == MovementKind.Reconciliation)
            {
                if (row.Recount(state))
                {
                    changed.Add((row, old));
                }

                state = row.State;
                continue;
            }

            decimal change;
            try
            {
                (state, change) = row.Kind switch
                {
                    MovementKind.Receipt => Valuation.Receive(state, row.ActualQty, row.ValueChange, method),
                    MovementKind.TransferIn => Valuation.Receive(state, row.ActualQty, transferInValue(row) ?? row.ValueChange, method),
                    _ => Valuation.Issue(state, -row.ActualQty, method),
                };
            }
            catch (DomainException ex) when (ex.Code == "stock.negative")
            {
                throw new DomainException("stock.negative", $"{row.VoucherNumber} on {row.PostingDate:yyyy-MM-dd} would take the stock below zero: {ex.Message}");
            }

            if (row.Revalue(change, state))
            {
                changed.Add((row, old));
            }
        }

        return changed;
    }
}

/// <summary>Where an item stands in a warehouse on the posting date (after every movement up to then).</summary>
public sealed record StockPosition(ValuationState State, ValuationMethod? Method);

public sealed record StockPostingResult(IReadOnlyList<StockLedgerEntry> Rows, IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit)> JournalLines);

/// <summary>
/// Turns a stock entry into ledger rows and the value journal, from the state of each item and warehouse on the posting
/// date. Later movements (a backdated entry) are repriced afterwards by <see cref="Reposting"/>.
/// </summary>
public static class StockPosting
{
    public static StockPostingResult Post(StockEntry entry, IReadOnlyDictionary<(Guid Item, Guid Warehouse), StockPosition> positions,
        IReadOnlyDictionary<Guid, ValuationMethod> methods, IReadOnlyDictionary<Guid, Warehouse> warehouses, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(warehouses);
        if (entry.PostingDate > today)
        {
            throw new DomainException("stock.future", "Stock cannot be posted on a future date.");
        }

        var state = new Dictionary<(Guid, Guid), ValuationState>();
        ValuationState Current(Guid item, Guid warehouse)
        {
            if (state.TryGetValue((item, warehouse), out var s))
            {
                return s;
            }

            return positions.GetValueOrDefault((item, warehouse))?.State ?? ValuationState.Empty;
        }

        var rows = new List<StockLedgerEntry>();
        var signed = new Dictionary<string, decimal>(StringComparer.Ordinal);
        void Book(string account, decimal amount) => signed[account] = signed.GetValueOrDefault(account) + amount;
        var values = new List<decimal>();

        for (var i = 0; i < entry.Lines.Count; i++)
        {
            var l = entry.Lines[i];
            var method = methods.GetValueOrDefault(l.ItemId, ValuationMethod.MovingAverage);
            decimal value;
            if (entry.Purpose == StockEntryPurpose.Reconciliation)
            {
                var target = l.TargetWarehouseId!.Value;
                var before = Current(l.ItemId, target);
                var rate = l.Rate ?? (before.Qty > 0m ? before.Rate : throw new DomainException("stock.count_rate",
                    $"{l.ItemCode} has no stock in {warehouses[target].Code} to take a rate from; give the rate for the count."));
                var countedValue = Rounding.ToCurrency(l.Qty * rate);
                var after = new ValuationState(l.Qty, countedValue, l.Qty > 0m && method == ValuationMethod.Fifo ? [new FifoLayer(l.Qty, countedValue / l.Qty)] : []);
                state[(l.ItemId, target)] = after;
                value = countedValue - before.Value;
                rows.Add(StockLedgerEntry.Create(entry, i, l.ItemId, target, l.Qty - before.Qty, value, after, method, MovementKind.Reconciliation));
                Book(warehouses[target].StockAccountCode, value);
                Book(entry.CounterAccountCode, -value);
            }
            else if (entry.Purpose == StockEntryPurpose.Receipt)
            {
                var target = l.TargetWarehouseId!.Value;
                var (after, change) = Valuation.Receive(Current(l.ItemId, target), l.Qty, l.Qty * l.Rate!.Value, method);
                state[(l.ItemId, target)] = after;
                rows.Add(StockLedgerEntry.Create(entry, i, l.ItemId, target, l.Qty, change, after, method, MovementKind.Receipt));
                value = change;
                Book(warehouses[target].StockAccountCode, value);
                Book(entry.CounterAccountCode, -value);
            }
            else
            {
                var source = l.SourceWarehouseId!.Value;
                ValuationState before;
                try
                {
                    before = Current(l.ItemId, source);
                    var (after, change) = Valuation.Issue(before, l.Qty, method);
                    state[(l.ItemId, source)] = after;
                    rows.Add(StockLedgerEntry.Create(entry, i, l.ItemId, source, -l.Qty, change, after, method,
                        entry.Purpose == StockEntryPurpose.Issue ? MovementKind.Issue : MovementKind.TransferOut));
                    value = -change;
                }
                catch (DomainException ex) when (ex.Code == "stock.negative")
                {
                    throw new DomainException("stock.negative", $"{l.ItemCode} in {warehouses[source].Code}: {ex.Message}");
                }

                Book(warehouses[source].StockAccountCode, -value);
                if (entry.Purpose == StockEntryPurpose.Issue)
                {
                    Book(entry.CounterAccountCode, value);
                }
                else
                {
                    var target = l.TargetWarehouseId!.Value;
                    var (inAfter, inChange) = Valuation.Receive(Current(l.ItemId, target), l.Qty, value, method);
                    state[(l.ItemId, target)] = inAfter;
                    rows.Add(StockLedgerEntry.Create(entry, i, l.ItemId, target, l.Qty, inChange, inAfter, method, MovementKind.TransferIn));
                    Book(warehouses[target].StockAccountCode, value);
                }
            }

            values.Add(value);
        }

        entry.SetLineValues(values);
        var journal = signed.Where(kv => kv.Value != 0m).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, Math.Max(kv.Value, 0m), Math.Max(-kv.Value, 0m))).ToList();
        return new StockPostingResult(rows, journal);
    }
}

public interface IStockRepository
{
    Task<IReadOnlyList<Warehouse>> ListWarehousesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<Warehouse?> GetWarehouseAsync(Guid id, CancellationToken cancellationToken);
    void Add(Warehouse warehouse);

    Task<IReadOnlyDictionary<Guid, StockItemSetting>> SettingsAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(StockItemSetting setting);

    /// <summary>Serialises postings per item and warehouse for the rest of the transaction.</summary>
    Task LockAsync(IEnumerable<(Guid Item, Guid Warehouse)> pairs, CancellationToken cancellationToken);

    /// <summary>
    /// The latest live row of one item and warehouse at or before a point in posting order (date, then sequence), tracked.
    /// One indexed row, however long the history.
    /// </summary>
    Task<StockLedgerEntry?> LastAtOrBeforeAsync(Guid itemId, Guid warehouseId, DateOnly date, long sequence, CancellationToken cancellationToken);

    /// <summary>The live rows of one item and warehouse after a point in posting order, tracked, oldest first (what reposting replays).</summary>
    Task<List<StockLedgerEntry>> RowsAfterAsync(Guid itemId, Guid warehouseId, DateOnly date, long sequence, CancellationToken cancellationToken);

    /// <summary>The transfer-in row paired with a transfer out (same entry and line), tracked.</summary>
    Task<StockLedgerEntry?> TransferInAsync(Guid voucherId, int lineIndex, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockEntry>> GetEntriesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<bool> HasMovementsAsync(Guid itemId, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockLedgerEntry>> RowsForVoucherAsync(Guid voucherId, CancellationToken cancellationToken);

    /// <summary>The latest live row of each item and warehouse dated on or before <paramref name="asOf"/>.</summary>
    Task<IReadOnlyList<StockLedgerEntry>> BalancesAsync(Guid companyId, DateOnly asOf, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockLedgerEntry>> LedgerAsync(Guid companyId, Guid itemId, Guid? warehouseId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    void Add(IEnumerable<StockLedgerEntry> rows);

    Task<StockEntry?> GetEntryAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<StockEntry>> ListEntriesAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    void Add(StockEntry entry);
}
