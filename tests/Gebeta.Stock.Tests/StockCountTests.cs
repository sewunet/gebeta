using Gebeta.SharedKernel;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Tests;

/// <summary>Stock counts (reconciliation): the counted quantity and value are kept; the difference is what gets booked.</summary>
public class StockCountTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Teff = Guid.CreateVersion7();
    private static readonly Warehouse Main = Warehouse.Create(Company, "MAIN", "Main", "1300").Value;
    private static readonly Dictionary<Guid, Warehouse> Warehouses = new() { [Main.Id] = Main };
    private static readonly DateOnly Day = new(2026, 11, 20);

    private static StockPostingResult Count(decimal qty, decimal? rate, ValuationState before, ValuationMethod method = ValuationMethod.MovingAverage, string? counter = null)
    {
        var entry = StockEntry.Create(Company, StockEntryPurpose.Reconciliation, Day, [new StockEntryLine(Teff, "TEFF", "Teff", "quintal", qty, rate, null, Main.Id)], counter, null);
        entry.AssignName("STE-COUNT");
        return StockPosting.Post(entry, new Dictionary<(Guid, Guid), StockPosition> { [(Teff, Main.Id)] = new(before, method) },
            new Dictionary<Guid, ValuationMethod> { [Teff] = method }, Warehouses, Day);
    }

    private static readonly ValuationState Ten = new(10m, 60_000m, []); // 10 quintals at 6,000

    [Fact]
    public void A_count_short_of_the_books_writes_off_the_difference_at_the_current_rate()
    {
        var result = Count(8m, null, Ten);
        var row = Assert.Single(result.Rows);
        Assert.Equal((-2m, -12_000m, 8m, 48_000m), (row.ActualQty, row.ValueChange, row.QtyAfter, row.ValueAfter));
        Assert.Equal([("1300", 0m, 12_000m), ("5100", 12_000m, 0m)], result.JournalLines);
    }

    [Fact]
    public void Opening_stock_is_a_count_with_a_rate_against_the_chosen_account()
    {
        var result = Count(25m, 5_800m, ValuationState.Empty, ValuationMethod.Fifo, counter: "3100");
        var row = Assert.Single(result.Rows);
        Assert.Equal((25m, 145_000m), (row.QtyAfter, row.ValueAfter));
        Assert.Equal([new FifoLayer(25m, 5_800m)], row.Queue);
        Assert.Equal([("1300", 145_000m, 0m), ("3100", 0m, 145_000m)], result.JournalLines);
    }

    [Fact]
    public void A_count_needs_a_rate_when_there_is_nothing_to_take_it_from_and_counts_each_item_once()
    {
        Assert.Equal("stock.count_rate", Assert.Throws<DomainException>(() => Count(5m, null, ValuationState.Empty)).Code);
        var line = new StockEntryLine(Teff, "TEFF", "Teff", "quintal", 1m, 1m, null, Main.Id);
        Assert.Equal("stock.count_duplicate", Assert.Throws<DomainException>(() =>
            StockEntry.Create(Company, StockEntryPurpose.Reconciliation, Day, [line, line], null, null)).Code);
        Assert.Equal(0m, Count(0m, null, Ten).Rows.Single().QtyAfter); // writing everything off is allowed
    }

    [Fact]
    public void Reposting_keeps_the_count_and_changes_its_difference()
    {
        var count = Count(8m, null, Ten).Rows.Single(); // books had 10 at 6,000; counted 8 → value 48,000
        // An earlier receipt of 2 at 6,000 is entered later: the books had 12 before the count.
        var changed = Reposting.Replay(new ValuationState(12m, 72_000m, []), [count], ValuationMethod.MovingAverage, _ => null);

        var (row, old) = Assert.Single(changed);
        Assert.Equal(-12_000m, old);
        Assert.Equal((-4m, -24_000m), (row.ActualQty, row.ValueChange)); // now 4 short
        Assert.Equal((8m, 48_000m), (row.QtyAfter, row.ValueAfter));     // the count itself stands
    }
}
