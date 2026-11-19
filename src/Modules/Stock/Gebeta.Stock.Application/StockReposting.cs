using Gebeta.SharedKernel;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Application;

/// <summary>
/// Reposting after a movement is posted, cancelled or a receipt's value changes (a landed cost): replays the later ledger rows of each
/// item and warehouse and sends an adjustment journal for every entry whose value changed. Shared by the stock entry and landed cost handlers.
/// </summary>
public sealed class StockReposter(IStockRepository stock, IStockUnitOfWork unitOfWork, TimeProvider clock)
{
    private static void Add(Dictionary<string, decimal> signed, string account, decimal amount) => signed[account] = signed.GetValueOrDefault(account) + amount;

    /// <summary>
    /// Replays each seeded item and warehouse after its key; a transfer out whose value changed carries the new value to its
    /// transfer in, whose warehouse is replayed in turn. Changed issues and transfers get their entry line revalued, and
    /// each changed entry gets an adjustment journal for the difference.
    /// </summary>
    public async Task RepostAsync(IReadOnlyCollection<(Guid Item, Guid Warehouse)> triggerPairs, string triggerName, IEnumerable<((Guid Item, Guid Warehouse) Pair, (DateOnly Date, long Sequence) After)> seeds,
        Dictionary<Guid, Warehouse> warehouses, IReadOnlyDictionary<Guid, StockItemSetting> settings, IReadOnlyDictionary<Guid, ValuationMethod>? methods,
        CancellationToken cancellationToken)
    {
        var work = new Queue<((Guid Item, Guid Warehouse) Pair, (DateOnly Date, long Sequence) After)>(seeds);
        var transferValues = new Dictionary<(Guid Voucher, int Line), decimal>();
        var changed = new Dictionary<Guid, (StockLedgerEntry Row, decimal Old)>();
        var locked = new HashSet<(Guid, Guid)>(triggerPairs);
        for (var guard = 0; work.Count > 0; guard++)
        {
            if (guard > 1_000)
            {
                throw new DomainException("stock.repost_loop", "Reposting did not settle; check transfers going back and forth.");
            }

            var (pair, after) = work.Dequeue();
            if (locked.Add(pair))
            {
                await stock.LockAsync([pair], cancellationToken).ConfigureAwait(false);
            }

            // Only the rows after the point and the one row before it: the cost follows what changed, not the item's history.
            // Rows are tracked, so a pair replayed twice sees its own earlier changes.
            var rows = await stock.RowsAfterAsync(pair.Item, pair.Warehouse, after.Date, after.Sequence, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                continue;
            }

            var before = await stock.LastAtOrBeforeAsync(pair.Item, pair.Warehouse, after.Date, after.Sequence, cancellationToken).ConfigureAwait(false);
            var method = methods?.GetValueOrDefault(pair.Item) ?? settings.GetValueOrDefault(pair.Item)?.Method ?? rows[0].Method;
            var replayed = Reposting.Replay(before?.State ?? ValuationState.Empty, rows, method,
                r => transferValues.TryGetValue((r.VoucherId, r.LineIndex), out var v) ? v : null);
            foreach (var (row, old) in replayed)
            {
                changed.TryAdd(row.Id, (row, old));
                if (row.Kind != MovementKind.TransferOut)
                {
                    continue;
                }

                transferValues[(row.VoucherId, row.LineIndex)] = -row.ValueChange;
                var transferIn = await stock.TransferInAsync(row.VoucherId, row.LineIndex, cancellationToken).ConfigureAwait(false)
                    ?? throw new DomainException("stock.repost_transfer", $"{row.VoucherNumber} has no matching transfer in.");
                work.Enqueue(((transferIn.ItemId, transferIn.WarehouseId), (transferIn.PostingDate, transferIn.Sequence - 1)));
            }
        }

        var real = changed.Values.Where(c => c.Row.ValueChange != c.Old).ToList();
        if (real.Count == 0)
        {
            return;
        }

        var vouchers = (await stock.GetEntriesAsync(real.Select(c => c.Row.VoucherId).Distinct(), cancellationToken).ConfigureAwait(false)).ToDictionary(e => e.Id);
        foreach (var group in real.GroupBy(c => c.Row.VoucherId))
        {
            var voucher = vouchers[group.Key];
            var signed = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var (row, old) in group)
            {
                Add(signed, warehouses[row.WarehouseId].StockAccountCode, row.ValueChange - old);
                if (row.Kind is MovementKind.Issue or MovementKind.TransferOut)
                {
                    voucher.RevalueLine(row.LineIndex, -row.ValueChange);
                }
                else if (row.Kind == MovementKind.Reconciliation)
                {
                    voucher.RevalueLine(row.LineIndex, row.ValueChange); // the count's difference
                }
            }

            if (voucher.Purpose is StockEntryPurpose.Issue or StockEntryPurpose.Reconciliation)
            {
                Add(signed, voucher.CounterAccountCode, -group.Sum(c => c.Row.ValueChange - c.Old));
            }

            voucher.ModifiedAt = clock.GetUtcNow();
            PublishAdjustment(voucher.Id, voucher.Name!, voucher.CompanyId, voucher.PostingDate, signed, $"Stock revaluation of {voucher.Name} after {triggerName}");
        }
    }

    public void PublishAdjustment(Guid voucherId, string voucherNumber, Guid companyId, DateOnly postingDate, Dictionary<string, decimal> accountChanges, string remark)
    {
        var lines = accountChanges.Where(kv => kv.Value != 0m).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new StockJournalLine(kv.Key, Math.Max(kv.Value, 0m), Math.Max(-kv.Value, 0m))).ToList();
        if (lines.Count > 0)
        {
            unitOfWork.Publish(new StockJournalAdjusted(Guid.CreateVersion7(), voucherId, voucherNumber, companyId, postingDate, remark, lines, clock.GetUtcNow()));
        }
    }
}
