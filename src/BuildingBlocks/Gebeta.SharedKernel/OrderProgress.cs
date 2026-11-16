namespace Gebeta.SharedKernel;

/// <summary>How far an order has come: goods (received or delivered) and billing (invoiced).</summary>
public enum OrderProgressState
{
    Draft,
    ToFulfilAndBill,
    ToFulfil,
    ToBill,
    Completed,
    Closed,
    Cancelled,
}

/// <summary>
/// The fulfilment rules shared by purchase and sales orders, so the two never drift apart: a line counts as done when its
/// fulfilled (or billed) quantity reaches the ordered quantity; percentages count each line only up to what was ordered;
/// a closed order that is not yet complete shows as closed.
/// </summary>
public static class OrderProgress
{
    public static OrderProgressState StateOf(DocStatus status, bool isClosed, IReadOnlyCollection<(Guid ItemId, decimal Qty)> ordered,
        IReadOnlyDictionary<Guid, decimal> fulfilled, IReadOnlyDictionary<Guid, decimal> billed)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(fulfilled);
        ArgumentNullException.ThrowIfNull(billed);
        if (status == DocStatus.Draft)
        {
            return OrderProgressState.Draft;
        }

        if (status == DocStatus.Cancelled)
        {
            return OrderProgressState.Cancelled;
        }

        var allFulfilled = ordered.All(l => fulfilled.GetValueOrDefault(l.ItemId) >= l.Qty);
        var allBilled = ordered.All(l => billed.GetValueOrDefault(l.ItemId) >= l.Qty);
        if (allFulfilled && allBilled)
        {
            return OrderProgressState.Completed;
        }

        if (isClosed)
        {
            return OrderProgressState.Closed;
        }

        return (allFulfilled, allBilled) switch
        {
            (true, false) => OrderProgressState.ToBill,
            (false, true) => OrderProgressState.ToFulfil,
            _ => OrderProgressState.ToFulfilAndBill,
        };
    }

    /// <summary>Share of the ordered quantity covered (each line counted up to its ordered quantity), as a percentage with one decimal.</summary>
    public static decimal PercentOf(IReadOnlyCollection<(Guid ItemId, decimal Qty)> ordered, IReadOnlyDictionary<Guid, decimal> done)
    {
        ArgumentNullException.ThrowIfNull(ordered);
        ArgumentNullException.ThrowIfNull(done);
        var total = ordered.Sum(l => l.Qty);
        return total == 0m ? 0m : Math.Round(ordered.Sum(l => Math.Min(done.GetValueOrDefault(l.ItemId), l.Qty)) * 100m / total, 1);
    }
}
