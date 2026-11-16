using Gebeta.SharedKernel;

namespace Gebeta.Selling.Domain.Orders;

/// <summary>A line of a sales order. <see cref="WarehouseId"/> is where the goods should come from (a suggestion for the delivery).</summary>
public sealed record SalesOrderLine(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Qty, decimal Rate, Guid? WarehouseId)
{
    public decimal Amount => Rounding.ToCurrency(Qty * Rate);
}

public enum OrderState
{
    Draft,
    ToDeliverAndBill,
    ToDeliver,
    ToBill,
    Completed,
    Closed,
    Cancelled,
}

/// <summary>
/// An order from a customer. Submitting confirms it; goods are delivered from Stock and invoiced in Accounting with the order
/// number as their reference, and the order shows how far each line has come. Closing stops it short (nothing more expected).
/// </summary>
public sealed class SalesOrder : DocumentBase
{
    private List<SalesOrderLine> _lines = [];

    private SalesOrder()
    {
        CustomerName = string.Empty;
    }

    private SalesOrder(Guid companyId, DateOnly orderDate)
        : base(companyId, orderDate)
    {
        CustomerName = string.Empty;
    }

    public override string DocumentType => "Sales Order";

    public Guid CustomerId { get; private set; }
    public string CustomerName { get; private set; }
    public DateOnly DeliveryDate { get; private set; }
    public string? Remark { get; private set; }
    public bool IsClosed { get; private set; }
    public IReadOnlyList<SalesOrderLine> Lines => _lines;
    public decimal NetTotal => _lines.Sum(l => l.Amount);

    public static SalesOrder Create(Guid companyId, Guid customerId, string customerName, DateOnly orderDate, DateOnly requiredBy, IReadOnlyList<SalesOrderLine> lines,
        string? remark)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(l => l.Qty <= 0m || l.Rate < 0m))
        {
            throw new DomainException("selling.order_lines", "A sales order needs lines with a positive quantity and a rate.");
        }

        if (lines.Select(l => l.ItemId).Distinct().Count() != lines.Count)
        {
            throw new DomainException("selling.order_duplicate", "Each item appears once on an order; put the whole quantity on one line.");
        }

        if (requiredBy < orderDate)
        {
            throw new DomainException("selling.order_date", "The delivery date cannot be before the order date.");
        }

        return new SalesOrder(companyId, orderDate)
        {
            CustomerId = customerId, CustomerName = customerName, DeliveryDate = requiredBy, _lines = lines.ToList(),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    public void Close()
    {
        if (DocStatus != DocStatus.Submitted)
        {
            throw new DomainException("selling.order_close", "Only a submitted order can be closed.");
        }

        IsClosed = true;
    }

    public void Reopen()
    {
        if (!IsClosed)
        {
            throw new DomainException("selling.order_reopen", "The order is not closed.");
        }

        IsClosed = false;
    }

    private IReadOnlyCollection<(Guid ItemId, decimal Qty)> Ordered => _lines.Select(l => (l.ItemId, l.Qty)).ToList();

    /// <summary>Where the order stands, given what has been delivered and billed per item (the shared rules in <see cref="OrderProgress"/>).</summary>
    public OrderState StateFor(IReadOnlyDictionary<Guid, decimal> delivered, IReadOnlyDictionary<Guid, decimal> billed) =>
        OrderProgress.StateOf(DocStatus, IsClosed, Ordered, delivered, billed) switch
        {
            OrderProgressState.Draft => OrderState.Draft,
            OrderProgressState.Cancelled => OrderState.Cancelled,
            OrderProgressState.Completed => OrderState.Completed,
            OrderProgressState.Closed => OrderState.Closed,
            OrderProgressState.ToBill => OrderState.ToBill,
            OrderProgressState.ToFulfil => OrderState.ToDeliver,
            _ => OrderState.ToDeliverAndBill,
        };

    /// <summary>Share of the ordered quantity covered (each line counted up to its ordered quantity), as a percentage.</summary>
    public decimal PercentOf(IReadOnlyDictionary<Guid, decimal> done) => OrderProgress.PercentOf(Ordered, done);

    protected override void OnCancel()
    {
        IsClosed = false;
    }
}

public interface ISalesOrderRepository
{
    Task<SalesOrder?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesOrder>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    void Add(SalesOrder order);
}
