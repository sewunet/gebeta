using Gebeta.Buying.Domain.Orders;
using Gebeta.SharedKernel;

namespace Gebeta.Buying.Tests;

public class PurchaseOrderTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Cement = Guid.CreateVersion7();
    private static readonly Guid Sand = Guid.CreateVersion7();
    private static readonly DateOnly Day = new(2026, 11, 10);

    private static PurchaseOrder Order()
    {
        var o = PurchaseOrder.Create(Company, Guid.CreateVersion7(), "Addis Cement Factory", Day, Day.AddDays(7),
            [new PurchaseOrderLine(Cement, "CEM", "Cement", "bag", 100m, 500m, null), new PurchaseOrderLine(Sand, "SAND", "Sand", "m3", 20m, 300m, null)], null);
        o.AssignName("PO-1");
        return o;
    }

    private static Dictionary<Guid, decimal> Q(decimal cement, decimal sand) => new() { [Cement] = cement, [Sand] = sand };

    [Fact]
    public void The_state_follows_what_is_received_and_billed()
    {
        var o = Order();
        Assert.Equal(56_000m, o.NetTotal);
        Assert.Equal(OrderState.Draft, o.StateFor(Q(0, 0), Q(0, 0)));
        o.Submit();
        Assert.Equal(OrderState.ToReceiveAndBill, o.StateFor(Q(60, 0), Q(0, 0)));
        Assert.Equal(50m, o.PercentOf(Q(60, 0)));
        Assert.Equal(OrderState.ToBill, o.StateFor(Q(100, 20), Q(0, 0)));
        Assert.Equal(OrderState.ToReceive, o.StateFor(Q(0, 0), Q(100, 20)));
        Assert.Equal(OrderState.Completed, o.StateFor(Q(100, 20), Q(100, 20)));
        Assert.Equal(100m, o.PercentOf(Q(150, 20))); // receiving more than ordered counts up to the order
    }

    [Fact]
    public void A_closed_order_expects_nothing_more_until_reopened()
    {
        var o = Order();
        Assert.Throws<DomainException>(() => o.Close()); // still a draft
        o.Submit();
        o.Close();
        Assert.Equal(OrderState.Closed, o.StateFor(Q(60, 0), Q(60, 0)));
        o.Reopen();
        Assert.Equal(OrderState.ToReceiveAndBill, o.StateFor(Q(60, 0), Q(60, 0)));
        Assert.Throws<DomainException>(() => o.Reopen());
    }

    [Fact]
    public void An_order_needs_distinct_items_quantities_and_a_sensible_date()
    {
        var line = new PurchaseOrderLine(Cement, "CEM", "Cement", "bag", 1m, 1m, null);
        Assert.Throws<DomainException>(() => PurchaseOrder.Create(Company, Guid.Empty, "S", Day, Day, [], null));
        Assert.Throws<DomainException>(() => PurchaseOrder.Create(Company, Guid.Empty, "S", Day, Day, [line, line], null));
        Assert.Throws<DomainException>(() => PurchaseOrder.Create(Company, Guid.Empty, "S", Day, Day.AddDays(-1), [line], null));
        Assert.Throws<DomainException>(() => PurchaseOrder.Create(Company, Guid.Empty, "S", Day, Day, [line with { Qty = 0m }], null));
    }
}
