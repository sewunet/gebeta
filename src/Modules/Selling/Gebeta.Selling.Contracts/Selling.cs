namespace Gebeta.Selling.Contracts;

public static class SellingPermissions
{
    public const string OrdersRead = "selling.orders.read";
    public const string OrdersCreate = "selling.orders.create";
    public const string OrdersSubmit = "selling.orders.submit";

    public static IReadOnlyList<string> All { get; } = [OrdersRead, OrdersCreate, OrdersSubmit];
}

public sealed record SalesOrderLineDto(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Qty, decimal Rate, decimal Amount, Guid? WarehouseId,
    decimal DeliveredQty, decimal BilledQty);

/// <param name="State">Draft, ToDeliverAndBill, ToDeliver, ToBill, Completed, Closed or Cancelled.</param>
public sealed record SalesOrderDto(Guid Id, string? Number, Guid CompanyId, Guid CustomerId, string CustomerName, DateOnly OrderDate, DateOnly DeliveryDate, string Status,
    string State, IReadOnlyList<SalesOrderLineDto> Lines, decimal NetTotal, decimal PercentDelivered, decimal PercentBilled, string? Remark);

/// <summary>A quantity of an item fulfilled against an order (delivered, delivered or invoiced).</summary>
public sealed record OrderedItemQty(Guid ItemId, decimal Qty);

/// <summary>
/// Goods delivered against sales orders, by order number (stock issues to the customer whose reference is the order number).
/// Provided by Stock; without Stock, Selling knows of no deliveries.
/// </summary>
public interface ISalesDeliveryLookup
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> DeliveredAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken);
}

/// <summary>
/// Quantities invoiced against sales orders, by order number (submitted sales invoices naming the order; credit notes
/// count negative). Provided by Accounting; without Accounting, Selling knows of no bills.
/// </summary>
public interface ISalesBillingLookup
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orderNumbers, CancellationToken cancellationToken);
}
