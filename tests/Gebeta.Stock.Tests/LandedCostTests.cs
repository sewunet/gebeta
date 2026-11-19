using Gebeta.SharedKernel;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Tests;

/// <summary>Landed costs: how a charge is shared across the lines of a receipt, and what is refused.</summary>
public class LandedCostTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Teff = Guid.CreateVersion7();
    private static readonly Guid Oil = Guid.CreateVersion7();
    private static readonly Warehouse Main = Warehouse.Create(Company, "MAIN", "Main", "1300").Value;
    private static readonly DateOnly Day = new(2026, 11, 10);

    // 10 quintals of teff at 100 = 1,000 and 5 drums of oil at 400 = 2,000.
    private static StockEntry Receipt(bool submit = true)
    {
        var entry = StockEntry.Create(Company, StockEntryPurpose.Receipt, Day,
            [new StockEntryLine(Teff, "TEFF", "Teff", "quintal", 10m, 100m, null, Main.Id), new StockEntryLine(Oil, "OIL", "Oil", "drum", 5m, 400m, null, Main.Id)], null, null);
        entry.AssignName("STE-1");
        if (submit)
        {
            entry.Submit();
        }

        return entry;
    }

    [Fact]
    public void Each_charge_is_shared_by_its_own_basis_and_the_shares_add_up()
    {
        var cost = LandedCost.Create(Receipt(), Day,
        [
            new LandedCharge("Freight", 300m, AllocationBasis.Value, ""),     // value 1,000 : 2,000 → 100 and 200
            new LandedCharge("Duty", 150m, AllocationBasis.Quantity, "2160"), // quantity 10 : 5 → 100 and 50
            new LandedCharge("Clearing", 100m, AllocationBasis.Weight, ""),   // weight 30 : 10 → 75 and 25
            new LandedCharge("LC charges", 50m, AllocationBasis.Volume, ""),  // volume 1 : 3 → 12.50 and 37.50
        ], [new LandedCostLineInput(0, 30m, 1m), new LandedCostLineInput(1, 10m, 3m)], null);
        Assert.Equal([100m + 100m + 75m + 12.5m, 200m + 50m + 25m + 37.5m], cost.Lines.Select(l => l.Allocated).ToArray()); // 287.50 and 312.50
        Assert.Equal(600m, cost.TotalCharges);
        Assert.Equal(["2150", "2150", "2150"], cost.Charges.Where(c => c.Description != "Duty").Select(c => c.AccountCode).ToArray()); // the default
        Assert.Equal("2160", cost.Charges.Single(c => c.Description == "Duty").AccountCode);
        Assert.Equal([1_000m, 2_000m], cost.Lines.Select(l => l.ReceivedValue).ToArray());
    }

    [Fact]
    public void Rounding_leftovers_go_to_the_largest_line_so_the_shares_add_up_exactly()
    {
        // 100 by three equal lines is 33.33 each and one cent left over: the first of the equals takes it.
        Assert.Equal([33.34m, 33.33m, 33.33m], LandedCost.Split(100m, [1m, 1m, 1m]).ToArray());
        // 10 by 1 : 2 : 2 → 2.00, 4.00, 4.00 exactly; by 3 : 3 : 1 → 4.29, 4.29, 1.43 = 10.01, so the first loses a cent.
        Assert.Equal([4.28m, 4.29m, 1.43m], LandedCost.Split(10m, [3m, 3m, 1m]).ToArray());
        Assert.Equal(10m, LandedCost.Split(10m, [3m, 3m, 1m]).Sum());
    }

    [Fact]
    public void Refused_when_there_is_nothing_to_share_by_or_the_receipt_is_not_posted_or_the_date_is_before_it()
    {
        Assert.Equal("landed.basis", Assert.Throws<DomainException>(() =>
            LandedCost.Create(Receipt(), Day, [new LandedCharge("Clearing", 10m, AllocationBasis.Weight, "")], null, null)).Code);
        Assert.Equal("landed.receipt", Assert.Throws<DomainException>(() =>
            LandedCost.Create(Receipt(submit: false), Day, [new LandedCharge("Freight", 10m, AllocationBasis.Value, "")], null, null)).Code);
        Assert.Equal("landed.date", Assert.Throws<DomainException>(() =>
            LandedCost.Create(Receipt(), Day.AddDays(-1), [new LandedCharge("Freight", 10m, AllocationBasis.Value, "")], null, null)).Code);
        Assert.Equal("landed.charge", Assert.Throws<DomainException>(() =>
            LandedCost.Create(Receipt(), Day, [new LandedCharge("Freight", 0m, AllocationBasis.Value, "")], null, null)).Code);
        Assert.Equal("landed.charges", Assert.Throws<DomainException>(() => LandedCost.Create(Receipt(), Day, [], null, null)).Code);
    }
}
