using Gebeta.SharedKernel;

namespace Gebeta.Buying.Domain.Orders;

/// <summary>A line of a purchase order. <see cref="WarehouseId"/> is where the goods should arrive (a suggestion for the receipt).</summary>
public sealed record PurchaseOrderLine(Guid ItemId, string ItemCode, string ItemName, string Uom, decimal Qty, decimal Rate, Guid? WarehouseId)
{
    public decimal Amount => Rounding.ToCurrency(Qty * Rate);
}

public enum OrderState
{
    Draft,
    ToReceiveAndBill,
    ToReceive,
    ToBill,
    Completed,
    Closed,
    Cancelled,
}

/// <summary>
/// An order to a supplier. Submitting commits it; goods are received in Stock and billed in Accounting with the order
/// number as their reference, and the order shows how far each line has come. Closing stops it short (nothing more expected).
/// </summary>
public sealed class PurchaseOrder : DocumentBase
{
    private List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder()
    {
        SupplierName = string.Empty;
    }

    private PurchaseOrder(Guid companyId, DateOnly orderDate)
        : base(companyId, orderDate)
    {
        SupplierName = string.Empty;
    }

    public override string DocumentType => "Purchase Order";

    public Guid SupplierId { get; private set; }
    public string SupplierName { get; private set; }
    public DateOnly RequiredBy { get; private set; }
    public string? Remark { get; private set; }
    public bool IsClosed { get; private set; }
    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;
    public decimal NetTotal => _lines.Sum(l => l.Amount);

    public static PurchaseOrder Create(Guid companyId, Guid supplierId, string supplierName, DateOnly orderDate, DateOnly requiredBy, IReadOnlyList<PurchaseOrderLine> lines,
        string? remark)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0 || lines.Any(l => l.Qty <= 0m || l.Rate < 0m))
        {
            throw new DomainException("buying.order_lines", "A purchase order needs lines with a positive quantity and a rate.");
        }

        if (lines.Select(l => l.ItemId).Distinct().Count() != lines.Count)
        {
            throw new DomainException("buying.order_duplicate", "Each item appears once on an order; put the whole quantity on one line.");
        }

        if (requiredBy < orderDate)
        {
            throw new DomainException("buying.order_date", "The required-by date cannot be before the order date.");
        }

        return new PurchaseOrder(companyId, orderDate)
        {
            SupplierId = supplierId, SupplierName = supplierName, RequiredBy = requiredBy, _lines = lines.ToList(),
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    public void Close()
    {
        if (DocStatus != DocStatus.Submitted)
        {
            throw new DomainException("buying.order_close", "Only a submitted order can be closed.");
        }

        IsClosed = true;
    }

    public void Reopen()
    {
        if (!IsClosed)
        {
            throw new DomainException("buying.order_reopen", "The order is not closed.");
        }

        IsClosed = false;
    }

    /// <summary>Where the order stands, given what has been received and billed per item.</summary>
    public OrderState StateFor(IReadOnlyDictionary<Guid, decimal> received, IReadOnlyDictionary<Guid, decimal> billed)
    {
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(billed);
        if (DocStatus == DocStatus.Draft)
        {
            return OrderState.Draft;
        }

        if (DocStatus == DocStatus.Cancelled)
        {
            return OrderState.Cancelled;
        }

        var allReceived = _lines.All(l => received.GetValueOrDefault(l.ItemId) >= l.Qty);
        var allBilled = _lines.All(l => billed.GetValueOrDefault(l.ItemId) >= l.Qty);
        if (allReceived && allBilled)
        {
            return OrderState.Completed;
        }

        if (IsClosed)
        {
            return OrderState.Closed;
        }

        return (allReceived, allBilled) switch
        {
            (true, false) => OrderState.ToBill,
            (false, true) => OrderState.ToReceive,
            _ => OrderState.ToReceiveAndBill,
        };
    }

    /// <summary>Share of the ordered quantity covered (each line counted up to its ordered quantity), as a percentage.</summary>
    public decimal PercentOf(IReadOnlyDictionary<Guid, decimal> done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var ordered = _lines.Sum(l => l.Qty);
        return ordered == 0m ? 0m : Math.Round(_lines.Sum(l => Math.Min(done.GetValueOrDefault(l.ItemId), l.Qty)) * 100m / ordered, 1);
    }

    protected override void OnCancel()
    {
        IsClosed = false;
    }
}

public interface IPurchaseOrderRepository
{
    Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PurchaseOrder>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    void Add(PurchaseOrder order);
}
