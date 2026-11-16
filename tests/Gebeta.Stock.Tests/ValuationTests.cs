using Gebeta.SharedKernel;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Tests;

/// <summary>Golden tests for the valuation engine (docs/modules/stock.md, tests that must exist).</summary>
public class ValuationTests
{
    private static (ValuationState S, decimal Change) In(ValuationState s, decimal qty, decimal rate, ValuationMethod m) => Valuation.Receive(s, qty, qty * rate, m);

    [Fact]
    public void Moving_average_golden_sequence()
    {
        const ValuationMethod m = ValuationMethod.MovingAverage;
        var s = ValuationState.Empty;
        (s, _) = In(s, 10m, 100m, m);
        (s, _) = In(s, 10m, 130m, m);
        Assert.Equal((20m, 2_300m, 115m), (s.Qty, s.Value, s.Rate));

        (s, var out1) = Valuation.Issue(s, 5m, m);
        Assert.Equal(-575m, out1);
        Assert.Equal((15m, 1_725m), (s.Qty, s.Value));

        (s, _) = In(s, 5m, 120m, m);
        Assert.Equal((20m, 2_325m, 116.25m), (s.Qty, s.Value, s.Rate));

        (s, var out2) = Valuation.Issue(s, 20m, m);
        Assert.Equal(-2_325m, out2);
        Assert.Equal(ValuationState.Empty.Qty, s.Qty);
        Assert.Equal(0m, s.Value);
    }

    [Fact]
    public void Fifo_golden_sequence_takes_the_oldest_layers_first()
    {
        const ValuationMethod m = ValuationMethod.Fifo;
        var s = ValuationState.Empty;
        (s, _) = In(s, 10m, 100m, m);
        (s, _) = In(s, 10m, 130m, m);

        (s, var out1) = Valuation.Issue(s, 15m, m);
        Assert.Equal(-1_650m, out1); // 10 × 100 + 5 × 130
        Assert.Equal([new FifoLayer(5m, 130m)], s.Queue);
        Assert.Equal(650m, s.Value);

        (s, _) = In(s, 5m, 120m, m);
        (s, var out2) = Valuation.Issue(s, 7m, m);
        Assert.Equal(-890m, out2); // 5 × 130 + 2 × 120
        Assert.Equal([new FifoLayer(3m, 120m)], s.Queue);
        Assert.Equal((3m, 360m), (s.Qty, s.Value));
    }

    [Fact]
    public void Rounding_never_leaves_value_behind_when_the_stock_is_used_up()
    {
        var s = Valuation.Receive(ValuationState.Empty, 3m, 100m, ValuationMethod.MovingAverage).State; // 33.333… each
        var total = 0m;
        for (var i = 0; i < 3; i++)
        {
            (s, var change) = Valuation.Issue(s, 1m, ValuationMethod.MovingAverage);
            total -= change;
        }

        Assert.Equal(100m, total);
        Assert.Equal(0m, s.Value);
    }

    [Fact]
    public void Stock_cannot_go_negative()
    {
        var s = In(ValuationState.Empty, 2m, 50m, ValuationMethod.Fifo).S;
        Assert.Equal("stock.negative", Assert.Throws<DomainException>(() => Valuation.Issue(s, 3m, ValuationMethod.Fifo)).Code);
        Assert.Throws<DomainException>(() => Valuation.Receive(s, 0m, 0m, ValuationMethod.Fifo));
    }
}
