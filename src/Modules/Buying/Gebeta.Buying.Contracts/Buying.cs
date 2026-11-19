namespace Gebeta.Buying.Contracts;

public static class BuyingPermissions
{
    public const string OrdersRead = "buying.orders.read";
    public const string OrdersCreate = "buying.orders.create";
    public const string OrdersSubmit = "buying.orders.submit";

    public const string AgreementsRead = "buying.agreements.read";
    public const string AgreementsCreate = "buying.agreements.create";
    public const string AgreementsSubmit = "buying.agreements.submit";

    public static IReadOnlyList<string> All { get; } = [OrdersRead, OrdersCreate, OrdersSubmit, AgreementsRead, AgreementsCreate, AgreementsSubmit];
}

public sealed record PurchaseOrderLineDto(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Qty, decimal Rate, decimal Amount, Guid? WarehouseId,
    decimal ReceivedQty, decimal BilledQty);

/// <param name="State">Draft, ToReceiveAndBill, ToReceive, ToBill, Completed, Closed or Cancelled.</param>
public sealed record PurchaseOrderDto(Guid Id, string? Number, Guid CompanyId, Guid SupplierId, string SupplierName, DateOnly OrderDate, DateOnly RequiredBy, string Status,
    string State, IReadOnlyList<PurchaseOrderLineDto> Lines, decimal NetTotal, decimal PercentReceived, decimal PercentBilled, string? Remark,
    Guid? AgreementId = null, string? AgreementNumber = null);

public sealed record AgreementLineDto(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Rate, decimal? PlannedQty, decimal CalledQty, decimal CalledAmount,
    decimal? RemainingQty);

/// <summary>A submitted order that has called off against an agreement.</summary>
public sealed record AgreementCallOffDto(Guid OrderId, string? Number, DateOnly OrderDate, decimal Amount, string State);

/// <param name="State">Draft, Active, Exhausted, Expired or Cancelled.</param>
/// <param name="AmountLimit">The agreed amount for the whole agreement; null when only quantities are planned.</param>
/// <param name="RemainingAmount">What is left of <paramref name="AmountLimit"/>; null without a limit.</param>
public sealed record BlanketAgreementDto(Guid Id, string? Number, Guid CompanyId, Guid SupplierId, string SupplierName, DateOnly AgreementDate, DateOnly ValidFrom, DateOnly ValidTo,
    string Status, string State, decimal? AmountLimit, decimal CalledAmount, decimal? RemainingAmount, IReadOnlyList<AgreementLineDto> Lines,
    IReadOnlyList<AgreementCallOffDto> CallOffs, string? Remark);

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
