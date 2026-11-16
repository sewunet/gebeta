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

    /// <summary>The other side of the value journal for receipts and issues (default 5100 Stock Adjustment).</summary>
    public string CounterAccountCode { get; private set; }

    public IReadOnlyList<StockEntryLine> Lines => _lines;
    public decimal TotalValue => _lines.Sum(l => l.Value);

    public static StockEntry Create(Guid companyId, StockEntryPurpose purpose, DateOnly postingDate, IReadOnlyList<StockEntryLine> lines, string? counterAccountCode, string? remark)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
        {
            throw new DomainException("stock.entry_lines", "A stock entry needs at least one line.");
        }

        foreach (var l in lines)
        {
            if (l.Qty <= 0m || decimal.Round(l.Qty, 3) != l.Qty)
            {
                throw new DomainException("stock.entry_qty", $"{l.ItemCode}: the quantity must be positive, with at most three decimals.");
            }

            var ok = purpose switch
            {
                StockEntryPurpose.Receipt => l.TargetWarehouseId is not null && l.SourceWarehouseId is null && l.Rate is >= 0m,
                StockEntryPurpose.Issue => l.SourceWarehouseId is not null && l.TargetWarehouseId is null,
                _ => l.SourceWarehouseId is not null && l.TargetWarehouseId is not null && l.SourceWarehouseId != l.TargetWarehouseId,
            };
            if (!ok)
            {
                throw new DomainException("stock.entry_warehouse", purpose switch
                {
                    StockEntryPurpose.Receipt => $"{l.ItemCode}: a receipt needs a target warehouse and a rate.",
                    StockEntryPurpose.Issue => $"{l.ItemCode}: an issue needs a source warehouse.",
                    _ => $"{l.ItemCode}: a transfer needs two different warehouses.",
                });
            }
        }

        return new StockEntry(companyId, postingDate)
        {
            Purpose = purpose, _lines = lines.Select(l => l with { Value = 0m }).ToList(),
            CounterAccountCode = string.IsNullOrWhiteSpace(counterAccountCode) ? DefaultCounterAccount : counterAccountCode.Trim(),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    internal void SetLineValues(IReadOnlyList<decimal> values) => _lines = _lines.Select((l, i) => l with { Value = values[i] }).ToList();
}

/// <summary>
/// A row of the stock ledger: one movement of one item in one warehouse, with the state after it. Append-only; a cancelled
/// entry's rows are marked cancelled (allowed only while they are the latest rows of their item and warehouse).
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

    public Guid CompanyId { get; private set; }
    public Guid ItemId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public decimal ActualQty { get; private set; }
    public decimal ValueChange { get; private set; }
    public decimal QtyAfter { get; private set; }
    public decimal ValueAfter { get; private set; }
    public ValuationMethod Method { get; private set; }
    public IReadOnlyList<FifoLayer> Queue => _queue;
    public Guid VoucherId { get; private set; }
    public string VoucherNumber { get; private set; }
    public int LineIndex { get; private set; }
    public bool IsCancelled { get; private set; }

    /// <summary>The rate of this movement (value change per unit).</summary>
    public decimal Rate => ActualQty == 0m ? 0m : ValueChange / ActualQty;

    public ValuationState State => new(QtyAfter, ValueAfter, _queue);

    internal static StockLedgerEntry Create(StockEntry entry, int lineIndex, Guid itemId, Guid warehouseId, decimal qty, decimal valueChange, ValuationState after,
        ValuationMethod method) => new()
    {
        CompanyId = entry.CompanyId, ItemId = itemId, WarehouseId = warehouseId, PostingDate = entry.PostingDate, ActualQty = qty, ValueChange = valueChange,
        QtyAfter = after.Qty, ValueAfter = after.Value, Method = method, _queue = after.Queue.ToList(), VoucherId = entry.Id, VoucherNumber = entry.Name ?? string.Empty,
        LineIndex = lineIndex,
    };

    public void MarkCancelled() => IsCancelled = true;
}

/// <summary>Where an item stands in a warehouse before a posting: its state and the date of its latest movement.</summary>
public sealed record StockPosition(ValuationState State, DateOnly? LastMovement, ValuationMethod? Method);

public sealed record StockPostingResult(IReadOnlyList<StockLedgerEntry> Rows, IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit)> JournalLines);

/// <summary>Turns a stock entry into ledger rows and the value journal.</summary>
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

            var p = positions.GetValueOrDefault((item, warehouse));
            if (p?.LastMovement is { } last && entry.PostingDate < last)
            {
                throw new DomainException("stock.backdated",
                    $"There are later stock movements for this item in {warehouses[warehouse].Code} (on {last:yyyy-MM-dd}); backdated entries are not supported yet.");
            }

            return p?.State ?? ValuationState.Empty;
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
            if (entry.Purpose == StockEntryPurpose.Receipt)
            {
                var target = l.TargetWarehouseId!.Value;
                var (after, change) = Valuation.Receive(Current(l.ItemId, target), l.Qty, l.Qty * l.Rate!.Value, method);
                state[(l.ItemId, target)] = after;
                rows.Add(StockLedgerEntry.Create(entry, i, l.ItemId, target, l.Qty, change, after, method));
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
                    rows.Add(StockLedgerEntry.Create(entry, i, l.ItemId, source, -l.Qty, change, after, method));
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
                    rows.Add(StockLedgerEntry.Create(entry, i, l.ItemId, target, l.Qty, inChange, inAfter, method));
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

    /// <summary>The latest live ledger row of each pair.</summary>
    Task<IReadOnlyDictionary<(Guid Item, Guid Warehouse), StockLedgerEntry>> LatestAsync(IEnumerable<(Guid Item, Guid Warehouse)> pairs, CancellationToken cancellationToken);

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
