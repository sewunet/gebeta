using Gebeta.SharedKernel;

namespace Gebeta.Stock.Domain.Ledger;

/// <summary>How an item's stock is valued; chosen per item, and fixed once the item has stock movements.</summary>
public enum ValuationMethod
{
    MovingAverage,
    Fifo,
}

/// <summary>One FIFO layer: a quantity still in stock at the rate it came in.</summary>
public sealed record FifoLayer(decimal Qty, decimal Rate);

/// <summary>Stock of one item in one warehouse: quantity, value and (for FIFO) the layers still in stock.</summary>
public sealed record ValuationState(decimal Qty, decimal Value, IReadOnlyList<FifoLayer> Queue)
{
    public static ValuationState Empty { get; } = new(0m, 0m, []);

    public decimal Rate => Qty == 0m ? 0m : Value / Qty;
}

/// <summary>
/// The valuation engine (docs/modules/stock.md): pure functions from a state and a movement to the new state.
/// Incoming stock adds its value; outgoing stock takes its rate from the engine (moving average: the current average;
/// FIFO: the oldest layers first), never from the user. Values are kept to the cent; stock can never go negative.
/// </summary>
public static class Valuation
{
    public static (ValuationState State, decimal ValueChange) Receive(ValuationState state, decimal qty, decimal value, ValuationMethod method)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (qty <= 0m || value < 0m)
        {
            throw new DomainException("stock.receive", "Incoming stock needs a positive quantity and a value of zero or more.");
        }

        value = Rounding.ToCurrency(value);
        var queue = state.Queue.ToList();
        if (method == ValuationMethod.Fifo)
        {
            var rate = value / qty;
            if (queue.Count > 0 && queue[^1].Rate == rate)
            {
                queue[^1] = queue[^1] with { Qty = queue[^1].Qty + qty };
            }
            else
            {
                queue.Add(new FifoLayer(qty, rate));
            }
        }

        return (new ValuationState(state.Qty + qty, state.Value + value, method == ValuationMethod.Fifo ? queue : []), value);
    }

    /// <summary>Takes <paramref name="qty"/> out; returns the new state and the (negative) value change.</summary>
    public static (ValuationState State, decimal ValueChange) Issue(ValuationState state, decimal qty, ValuationMethod method)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (qty <= 0m)
        {
            throw new DomainException("stock.issue", "Outgoing stock needs a positive quantity.");
        }

        if (qty > state.Qty)
        {
            throw new DomainException("stock.negative", $"Only {state.Qty} in stock; cannot take out {qty}.");
        }

        var remaining = state.Qty - qty;
        if (remaining == 0m)
        {
            return (ValuationState.Empty, -state.Value);
        }

        if (method == ValuationMethod.MovingAverage)
        {
            var outValue = Rounding.ToCurrency(state.Value * qty / state.Qty);
            return (new ValuationState(remaining, state.Value - outValue, []), -outValue);
        }

        var queue = state.Queue.ToList();
        var take = qty;
        var value = 0m;
        while (take > 0m)
        {
            var layer = queue[0];
            var used = Math.Min(layer.Qty, take);
            value += used * layer.Rate;
            take -= used;
            if (used == layer.Qty)
            {
                queue.RemoveAt(0);
            }
            else
            {
                queue[0] = layer with { Qty = layer.Qty - used };
            }
        }

        var fifoOut = Rounding.ToCurrency(value);
        return (new ValuationState(remaining, state.Value - fifoOut, queue), -fifoOut);
    }
}
