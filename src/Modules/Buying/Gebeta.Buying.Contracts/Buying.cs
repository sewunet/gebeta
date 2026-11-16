namespace Gebeta.Buying.Contracts;

public static class BuyingPermissions
{
    public const string OrdersRead = "buying.orders.read";
    public const string OrdersCreate = "buying.orders.create";
    public const string OrdersSubmit = "buying.orders.submit";

    public static IReadOnlyList<string> All { get; } = [OrdersRead, OrdersCreate, OrdersSubmit];
}

public sealed record PurchaseOrderLineDto(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Qty, decimal Rate, decimal Amount, Guid? WarehouseId,
    decimal ReceivedQty, decimal BilledQty);

/// <param name="State">Draft, ToReceiveAndBill, ToReceive, ToBill, Completed, Closed or Cancelled.</param>
public sealed record PurchaseOrderDto(Guid Id, string? Number, Guid CompanyId, Guid SupplierId, string SupplierName, DateOnly OrderDate, DateOnly RequiredBy, string Status,
    string State, IReadOnlyList<PurchaseOrderLineDto> Lines, decimal NetTotal, decimal PercentReceived, decimal PercentBilled, string? Remark);

/// <summary>A quantity of an item fulfilled against an order (received, delivered or invoiced).</summary>
public sealed record OrderedItemQty(Guid ItemId, decimal Qty);

/// <summary>
/// Goods received against purchase orders, by order number (stock receipts whose reference is the order number).
/// Provided by Stock; without Stock, Buying knows of no receipts.
/// </summary>
public interface IPurchaseReceiptLookup
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> ReceivedAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken);
}

/// <summary>
/// Quantities billed against purchase orders, by order number (submitted purchase invoices naming the order; debit notes
/// count negative). Provided by Accounting; without Accounting, Buying knows of no bills.
/// </summary>
public interface IPurchaseBillingLookup
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken);
}
