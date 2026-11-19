using Gebeta.Buying.Domain.Agreements;
using Gebeta.Buying.Domain.Orders;
using Gebeta.SharedKernel;

namespace Gebeta.Buying.Tests;

public class BlanketAgreementTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Supplier = Guid.CreateVersion7();
    private static readonly Guid Cement = Guid.CreateVersion7();
    private static readonly Guid Sand = Guid.CreateVersion7();
    private static readonly DateOnly From = new(2026, 1, 1);
    private static readonly DateOnly To = new(2026, 12, 31);

    private static AgreementLine CementLine(decimal? planned) => new(Cement, "CEM", "Cement", "bag", 500m, planned);

    private static AgreementLine SandLine(decimal? planned) => new(Sand, "SAND", "Sand", "m3", 300m, planned);

    private static BlanketAgreement Agreement(decimal? limit = null, decimal? cementQty = 100m, decimal? sandQty = 20m, bool submit = true)
    {
        var a = BlanketAgreement.Create(Company, Supplier, "Addis Cement Factory", From, From, To, limit, [CementLine(cementQty), SandLine(sandQty)], null);
        a.AssignName("BPA-1");
        if (submit)
        {
            a.Submit();
        }

        return a;
    }

    private static PurchaseOrderLine Line(Guid item, decimal qty, decimal rate) =>
        new(item, item == Cement ? "CEM" : "SAND", "x", "u", qty, rate, null);

    private static CalledOff Called(Guid item, decimal qty, decimal rate) => new(item, qty, qty * rate);

    [Fact]
    public void An_order_may_take_up_to_what_remains_of_each_item()
    {
        var a = Agreement();
        CalledOff[] already = [Called(Cement, 60m, 500m)]; // 60 of 100 bags gone, 40 left

        Assert.Equal(40m, BlanketAgreement.RemainingQty(CementLine(100m), already));
        Assert.Equal(20m, BlanketAgreement.RemainingQty(SandLine(20m), already));
        a.CheckCallOff(Supplier, new DateOnly(2026, 6, 1), [Line(Cement, 40m, 500m), Line(Sand, 20m, 300m)], already);
        var ex = Assert.Throws<DomainException>(() => a.CheckCallOff(Supplier, new DateOnly(2026, 6, 1), [Line(Cement, 41m, 500m)], already));
        Assert.Equal("buying.agreement_overdrawn", ex.Code);
    }

    [Fact]
    public void Calls_off_are_summed_per_item_from_the_orders()
    {
        PurchaseOrder Order(decimal cement, decimal sand)
        {
            var o = PurchaseOrder.Create(Company, Supplier, "S", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1), [Line(Cement, cement, 500m), Line(Sand, sand, 300m)], null);
            return o;
        }

        // 10 + 5 bags = 15 at 500 = 7,500; 2 + 4 m3 = 6 at 300 = 1,800.
        var summary = BlanketAgreement.Summarise([Order(10m, 2m), Order(5m, 4m)]);
        Assert.Equal((15m, 7_500m), (summary.Single(c => c.ItemId == Cement).Qty, summary.Single(c => c.ItemId == Cement).Amount));
        Assert.Equal((6m, 1_800m), (summary.Single(c => c.ItemId == Sand).Qty, summary.Single(c => c.ItemId == Sand).Amount));
    }

    [Fact]
    public void An_amount_limit_is_shared_by_all_items()
    {
        var a = Agreement(limit: 10_000m, cementQty: null, sandQty: null);
        CalledOff[] already = [Called(Cement, 10m, 500m), Called(Sand, 10m, 300m)]; // 5,000 + 3,000 = 8,000 gone, 2,000 left

        Assert.Equal(2_000m, a.RemainingAmount(already));
        a.CheckCallOff(Supplier, From, [Line(Cement, 4m, 500m)], already); // exactly 2,000
        Assert.Throws<DomainException>(() => a.CheckCallOff(Supplier, From, [Line(Sand, 7m, 300m)], already)); // 2,100
        Assert.Equal(0m, a.RemainingAmount([Called(Cement, 30m, 500m)])); // over-spent history never shows a negative balance
    }

    [Theory]
    [InlineData("other supplier", "buying.agreement_supplier")]
    [InlineData("day before", "buying.agreement_period_outside")]
    [InlineData("day after", "buying.agreement_period_outside")]
    [InlineData("other item", "buying.agreement_item")]
    [InlineData("other rate", "buying.agreement_rate")]
    public void An_order_that_does_not_match_the_agreement_is_refused(string what, string code)
    {
        var a = Agreement();
        var supplier = what == "other supplier" ? Guid.CreateVersion7() : Supplier;
        var date = what switch { "day before" => From.AddDays(-1), "day after" => To.AddDays(1), _ => From };
        var line = what switch { "other item" => Line(Guid.CreateVersion7(), 1m, 500m), "other rate" => Line(Cement, 1m, 499m), _ => Line(Cement, 1m, 500m) };

        Assert.Equal(code, Assert.Throws<DomainException>(() => a.CheckCallOff(supplier, date, [line], [])).Code);
        a.CheckCallOff(Supplier, To, [Line(Cement, 1m, 500m)], []); // the first and last day themselves are inside
        a.CheckCallOff(Supplier, From, [Line(Cement, 1m, 500m)], []);
    }

    [Fact]
    public void Only_a_submitted_agreement_takes_orders()
    {
        var draft = Agreement(submit: false);
        Assert.Equal("buying.agreement_not_active", Assert.Throws<DomainException>(() => draft.CheckCallOff(Supplier, From, [Line(Cement, 1m, 500m)], [])).Code);
        var cancelled = Agreement();
        cancelled.Cancel();
        Assert.Equal("buying.agreement_not_active", Assert.Throws<DomainException>(() => cancelled.CheckCallOff(Supplier, From, [Line(Cement, 1m, 500m)], [])).Code);
    }

    [Fact]
    public void The_state_follows_the_balance_and_the_calendar()
    {
        var inside = new DateOnly(2026, 6, 1);
        Assert.Equal(AgreementState.Draft, Agreement(submit: false).StateFor(inside, []));
        var a = Agreement();
        Assert.Equal(AgreementState.Active, a.StateFor(inside, [Called(Cement, 100m, 500m)])); // sand still to come
        Assert.Equal(AgreementState.Exhausted, a.StateFor(inside, [Called(Cement, 100m, 500m), Called(Sand, 20m, 300m)]));
        Assert.Equal(AgreementState.Expired, a.StateFor(To.AddDays(1), []));
        Assert.Equal(AgreementState.Active, a.StateFor(To, []));
        var capped = Agreement(limit: 1_000m, cementQty: null, sandQty: null);
        Assert.Equal(AgreementState.Exhausted, capped.StateFor(inside, [Called(Cement, 2m, 500m)]));
        var cancelled = Agreement();
        cancelled.Cancel();
        Assert.Equal(AgreementState.Cancelled, cancelled.StateFor(inside, []));
    }

    [Fact]
    public void An_agreement_needs_a_cap_a_sensible_period_and_distinct_items()
    {
        AgreementLine[] lines = [CementLine(null)];
        Assert.Throws<DomainException>(() => BlanketAgreement.Create(Company, Supplier, "S", From, From, To, null, lines, null)); // nothing capped
        BlanketAgreement.Create(Company, Supplier, "S", From, From, To, 1m, lines, null);
        Assert.Throws<DomainException>(() => BlanketAgreement.Create(Company, Supplier, "S", From, To, From, 1m, lines, null));
        Assert.Throws<DomainException>(() => BlanketAgreement.Create(Company, Supplier, "S", From, From, To, 0m, lines, null));
        Assert.Throws<DomainException>(() => BlanketAgreement.Create(Company, Supplier, "S", From, From, To, 1m, [], null));
        Assert.Throws<DomainException>(() => BlanketAgreement.Create(Company, Supplier, "S", From, From, To, 1m, [CementLine(null), CementLine(null)], null));
        Assert.Throws<DomainException>(() => BlanketAgreement.Create(Company, Supplier, "S", From, From, To, 1m, [CementLine(0m)], null));
    }
}
