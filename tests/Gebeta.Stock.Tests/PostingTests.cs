using Gebeta.SharedKernel;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Tests;

public class PostingTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Cement = Guid.CreateVersion7();
    private static readonly DateOnly Day = new(2026, 11, 12);
    private static readonly Warehouse Main = Warehouse.Create(Company, "MAIN", "Main store", "1300").Value;
    private static readonly Warehouse Shop = Warehouse.Create(Company, "SHOP", "Shop", "1310").Value;
    private static readonly Warehouse Annex = Warehouse.Create(Company, "ANNEX", "Annex", "1300").Value;
    private static readonly Dictionary<Guid, Warehouse> Warehouses = new[] { Main, Shop, Annex }.ToDictionary(w => w.Id);

    private static StockEntryLine Line(decimal qty, decimal? rate = null, Warehouse? from = null, Warehouse? to = null) =>
        new(Cement, "CEMENT", "Cement 50 kg", "bag", qty, rate, from?.Id, to?.Id);

    private static StockEntry Entry(StockEntryPurpose purpose, params StockEntryLine[] lines)
    {
        var e = StockEntry.Create(Company, purpose, Day, lines, null, null);
        e.AssignName($"STE-{Guid.NewGuid():N}");
        return e;
    }

    private static StockPostingResult Post(StockEntry e, Dictionary<(Guid, Guid), StockPosition>? positions = null) =>
        StockPosting.Post(e, positions ?? [], new Dictionary<Guid, ValuationMethod> { [Cement] = ValuationMethod.MovingAverage }, Warehouses, Day);

    [Fact]
    public void A_receipt_debits_the_warehouse_stock_account_and_credits_stock_adjustment()
    {
        var result = Post(Entry(StockEntryPurpose.Receipt, Line(100m, 500m, to: Main)));
        var row = Assert.Single(result.Rows);
        Assert.Equal((100m, 50_000m), (row.QtyAfter, row.ValueAfter));
        Assert.Equal([("1300", 50_000m, 0m), ("5100", 0m, 50_000m)], result.JournalLines);
    }

    [Fact]
    public void A_transfer_moves_value_between_stock_accounts_and_nothing_when_they_are_the_same()
    {
        var stocked = new Dictionary<(Guid, Guid), StockPosition> { [(Cement, Main.Id)] = new(new ValuationState(100m, 50_000m, []), Day, ValuationMethod.MovingAverage) };
        var toShop = Post(Entry(StockEntryPurpose.Transfer, Line(40m, from: Main, to: Shop)), stocked);
        Assert.Equal(2, toShop.Rows.Count);
        Assert.Equal([("1300", 0m, 20_000m), ("1310", 20_000m, 0m)], toShop.JournalLines);

        var toAnnex = Post(Entry(StockEntryPurpose.Transfer, Line(40m, from: Main, to: Annex)), stocked);
        Assert.Empty(toAnnex.JournalLines); // both warehouses carry stock on 1300
        Assert.Equal(20_000m, toAnnex.Rows.Single(r => r.WarehouseId == Annex.Id).ValueAfter);
    }

    [Fact]
    public void Backdated_future_and_negative_postings_are_refused()
    {
        var later = new Dictionary<(Guid, Guid), StockPosition> { [(Cement, Main.Id)] = new(new ValuationState(5m, 2_500m, []), Day.AddDays(1), ValuationMethod.MovingAverage) };
        Assert.Equal("stock.backdated", Assert.Throws<DomainException>(() => Post(Entry(StockEntryPurpose.Receipt, Line(1m, 1m, to: Main)), later)).Code);
        Assert.Equal("stock.negative", Assert.Throws<DomainException>(() => Post(Entry(StockEntryPurpose.Issue, Line(1m, from: Main)))).Code);

        var future = StockEntry.Create(Company, StockEntryPurpose.Receipt, Day.AddDays(1), [Line(1m, 1m, to: Main)], null, null);
        Assert.Equal("stock.future", Assert.Throws<DomainException>(() => Post(future)).Code);
    }

    [Fact]
    public void Each_purpose_needs_its_warehouses()
    {
        Assert.Throws<DomainException>(() => StockEntry.Create(Company, StockEntryPurpose.Receipt, Day, [Line(1m, null, to: Main)], null, null)); // no rate
        Assert.Throws<DomainException>(() => StockEntry.Create(Company, StockEntryPurpose.Issue, Day, [Line(1m, to: Main)], null, null));
        Assert.Throws<DomainException>(() => StockEntry.Create(Company, StockEntryPurpose.Transfer, Day, [Line(1m, from: Main, to: Main)], null, null));
        Assert.Throws<DomainException>(() => StockEntry.Create(Company, StockEntryPurpose.Receipt, Day, [Line(1.0005m, 1m, to: Main)], null, null));
    }
}
