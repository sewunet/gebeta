using System.Reflection;
using Gebeta.SharedKernel;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Tests;

/// <summary>Reposting: later movements repriced after a backdated or cancelled entry (docs/modules/stock.md).</summary>
public class RepostingTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Cement = Guid.CreateVersion7();
    private static readonly Warehouse Main = Warehouse.Create(Company, "MAIN", "Main store", "1300").Value;
    private static readonly Dictionary<Guid, Warehouse> Warehouses = new() { [Main.Id] = Main };

    /// <summary>Posts an entry on its own (as the handler does for the state on its date) and returns its row.</summary>
    private static StockLedgerEntry Row(StockEntryPurpose purpose, DateOnly date, decimal qty, decimal? rate, ValuationState before, ValuationMethod method)
    {
        var line = new StockEntryLine(Cement, "CEMENT", "Cement", "bag", qty, rate, purpose == StockEntryPurpose.Receipt ? null : Main.Id,
            purpose == StockEntryPurpose.Receipt ? Main.Id : null);
        var entry = StockEntry.Create(Company, purpose, date, [line], null, null);
        entry.AssignName($"STE-{date:MMdd}");
        var result = StockPosting.Post(entry, new Dictionary<(Guid, Guid), StockPosition> { [(Cement, Main.Id)] = new(before, method) },
            new Dictionary<Guid, ValuationMethod> { [Cement] = method }, Warehouses, new DateOnly(2026, 12, 31));
        return result.Rows.Single();
    }

    [Theory]
    [InlineData(ValuationMethod.MovingAverage, -5_500)] // 10 @ 500 + 10 @ 600 → average 550
    [InlineData(ValuationMethod.Fifo, -5_000)]          // the first 10 came in at 500
    public void A_backdated_receipt_reprices_a_later_issue(ValuationMethod method, decimal repricedIssue)
    {
        // Original order: receipt 10 @ 600 on the 12th, issue 10 on the 15th (value 6,000).
        var receipt = Row(StockEntryPurpose.Receipt, new DateOnly(2026, 11, 12), 10m, 600m, ValuationState.Empty, method);
        var issue = Row(StockEntryPurpose.Issue, new DateOnly(2026, 11, 15), 10m, null, receipt.State, method);
        Assert.Equal(-6_000m, issue.ValueChange);

        // Backdated receipt of 10 @ 500 on the 10th: replay the later rows from its state.
        var early = Row(StockEntryPurpose.Receipt, new DateOnly(2026, 11, 10), 10m, 500m, ValuationState.Empty, method);
        var changed = Reposting.Replay(early.State, [receipt, issue], method, _ => null);

        Assert.Equal(repricedIssue, issue.ValueChange);
        var issueChange = Assert.Single(changed, c => c.Row == issue);
        Assert.Equal(-6_000m, issueChange.OldValueChange);
        Assert.Equal((10m, 11_000m + repricedIssue), (issue.QtyAfter, issue.ValueAfter));
        Assert.Equal(repricedIssue + 6_000m, issue.ValueChange - issueChange.OldValueChange); // the adjustment journal's amount
    }

    [Fact]
    public void Cancelling_a_receipt_that_later_issues_needed_is_refused()
    {
        var receipt = Row(StockEntryPurpose.Receipt, new DateOnly(2026, 11, 12), 10m, 600m, ValuationState.Empty, ValuationMethod.MovingAverage);
        var issue = Row(StockEntryPurpose.Issue, new DateOnly(2026, 11, 15), 8m, null, receipt.State, ValuationMethod.MovingAverage);

        // Without the receipt the issue would take 8 out of nothing.
        var ex = Assert.Throws<DomainException>(() => Reposting.Replay(ValuationState.Empty, [issue], ValuationMethod.MovingAverage, _ => null));
        Assert.Equal("stock.negative", ex.Code);
        Assert.Contains("STE-1115", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transfer_in_takes_the_new_value_of_its_transfer_out()
    {
        var receipt = Row(StockEntryPurpose.Receipt, new DateOnly(2026, 11, 12), 10m, 600m, ValuationState.Empty, ValuationMethod.MovingAverage);
        var issue = Row(StockEntryPurpose.Issue, new DateOnly(2026, 11, 15), 5m, null, receipt.State, ValuationMethod.MovingAverage);
        var kind = typeof(StockLedgerEntry).GetProperty(nameof(StockLedgerEntry.Kind))!;
        kind.SetValue(receipt, MovementKind.TransferIn, BindingFlags.NonPublic | BindingFlags.Instance, null, null, null); // treat the receipt as a transfer in

        Reposting.Replay(ValuationState.Empty, [receipt, issue], ValuationMethod.MovingAverage, _ => 5_000m); // its transfer out is now worth 5,000

        Assert.Equal(5_000m, receipt.ValueChange);
        Assert.Equal(-2_500m, issue.ValueChange);
    }
}
