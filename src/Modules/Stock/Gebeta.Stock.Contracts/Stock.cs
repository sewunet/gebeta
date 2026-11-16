namespace Gebeta.Stock.Contracts;

public static class StockPermissions
{
    public const string WarehousesRead = "stock.warehouses.read";
    public const string WarehousesManage = "stock.warehouses.manage";
    public const string EntriesRead = "stock.entries.read";
    public const string EntriesCreate = "stock.entries.create";
    public const string EntriesSubmit = "stock.entries.submit";
    public const string ReportsRead = "stock.reports.read";

    public static IReadOnlyList<string> All { get; } = [WarehousesRead, WarehousesManage, EntriesRead, EntriesCreate, EntriesSubmit, ReportsRead];
}

public sealed record WarehouseDto(Guid Id, Guid CompanyId, string Code, string Name, string StockAccountCode);

public sealed record StockEntryLineDto(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Qty, decimal? Rate, Guid? SourceWarehouseId, string? SourceWarehouse,
    Guid? TargetWarehouseId, string? TargetWarehouse, decimal Value);

public sealed record StockEntryDto(Guid Id, string? Number, Guid CompanyId, string Purpose, DateOnly PostingDate, string Status, string CounterAccountCode, string? Remark,
    IReadOnlyList<StockEntryLineDto> Lines, decimal TotalValue);

public sealed record StockBalanceRow(Guid ItemId, string ItemCode, string ItemName, string Uom, Guid WarehouseId, string Warehouse, decimal Qty, decimal Rate, decimal Value,
    string Method);

public sealed record StockBalanceDto(Guid CompanyId, DateOnly AsOf, IReadOnlyList<StockBalanceRow> Rows, decimal TotalValue);

public sealed record StockLedgerRow(DateOnly Date, string Voucher, string Warehouse, decimal Qty, decimal Rate, decimal ValueChange, decimal QtyAfter, decimal ValueAfter);

public sealed record StockLedgerDto(Guid ItemId, string ItemCode, string ItemName, IReadOnlyList<StockLedgerRow> Rows);

public sealed record ItemStockSettingDto(Guid ItemId, string Method, bool HasMovements);

/// <summary>One line of the value journal Stock asks Accounting to post.</summary>
public sealed record StockJournalLine(string AccountCode, decimal Debit, decimal Credit);

/// <summary>A submitted stock entry's value journal (perpetual inventory). Accounting posts it when installed.</summary>
public sealed record StockJournalRequested(Guid SourceId, string SourceNumber, Guid CompanyId, DateOnly PostingDate, string Remark, IReadOnlyList<StockJournalLine> Lines,
    DateTimeOffset OccurredAt);

/// <summary>A stock entry was cancelled; Accounting reverses its value journal. No longer published (cancelling sends a <see cref="StockJournalAdjusted"/>); kept for events already queued.</summary>
public sealed record StockEntryCancelled(Guid SourceId, string SourceNumber, Guid CompanyId, DateTimeOffset OccurredAt);

/// <summary>
/// A change to stock value already posted: reposting after a backdated or cancelled entry repriced <see cref="VoucherNumber"/>,
/// or the entry itself was cancelled. Accounting posts it as its own journal (the general ledger stays append-only).
/// </summary>
public sealed record StockJournalAdjusted(Guid AdjustmentId, Guid VoucherId, string VoucherNumber, Guid CompanyId, DateOnly PostingDate, string Remark,
    IReadOnlyList<StockJournalLine> Lines, DateTimeOffset OccurredAt);
