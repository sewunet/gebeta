using Gebeta.Application;
using Gebeta.SharedKernel;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Application;

public interface IStockUnitOfWork : IUnitOfWork;

public interface IStockNumbers : IDocumentNumbers;

public sealed record ListWarehousesQuery(Guid CompanyId) : IQuery<IReadOnlyList<WarehouseDto>>;

public sealed record CreateWarehouseCommand(Guid CompanyId, string Code, string Name, string? StockAccountCode) : ICommand<WarehouseDto>;

public sealed record GetItemStockSettingQuery(Guid ItemId) : IQuery<ItemStockSettingDto>;

public sealed record SetItemValuationCommand(Guid ItemId, ValuationMethod Method) : ICommand<ItemStockSettingDto>;

public sealed record StockEntryLineInput(Guid ItemId, decimal Qty, decimal? Rate, Guid? SourceWarehouseId, Guid? TargetWarehouseId);

public sealed record CreateStockEntryCommand(Guid CompanyId, StockEntryPurpose Purpose, DateOnly PostingDate, IReadOnlyList<StockEntryLineInput> Lines,
    string? CounterAccountCode, string? Remark) : ICommand<StockEntryDto>;

public sealed record SubmitStockEntryCommand(Guid Id) : ICommand<StockEntryDto>;

public sealed record CancelStockEntryCommand(Guid Id) : ICommand<StockEntryDto>;

public sealed record GetStockEntryQuery(Guid Id) : IQuery<StockEntryDto>;

public sealed record ListStockEntriesQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<StockEntryDto>>;

public sealed record StockBalanceQuery(Guid CompanyId, DateOnly AsOf) : IQuery<StockBalanceDto>;

public sealed record StockLedgerQuery(Guid CompanyId, Guid ItemId, Guid? WarehouseId, DateOnly From, DateOnly To) : IQuery<StockLedgerDto>;

public sealed class StockHandler(
    IStockRepository stock,
    IItemDirectory items,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IStockNumbers numbers,
    IStockUnitOfWork unitOfWork,
    TimeProvider clock)
    : IQueryHandler<ListWarehousesQuery, IReadOnlyList<WarehouseDto>>,
      ICommandHandler<CreateWarehouseCommand, WarehouseDto>,
      IQueryHandler<GetItemStockSettingQuery, ItemStockSettingDto>,
      ICommandHandler<SetItemValuationCommand, ItemStockSettingDto>,
      ICommandHandler<CreateStockEntryCommand, StockEntryDto>,
      ICommandHandler<SubmitStockEntryCommand, StockEntryDto>,
      ICommandHandler<CancelStockEntryCommand, StockEntryDto>,
      IQueryHandler<GetStockEntryQuery, StockEntryDto>,
      IQueryHandler<ListStockEntriesQuery, IReadOnlyList<StockEntryDto>>,
      IQueryHandler<StockBalanceQuery, StockBalanceDto>,
      IQueryHandler<StockLedgerQuery, StockLedgerDto>
{
    public const string DefaultPattern = "STE-.FY.-.#####";

    public async Task<Result<IReadOnlyList<WarehouseDto>>> Handle(ListWarehousesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<WarehouseDto>>((await stock.ListWarehousesAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<Result<WarehouseDto>> Handle(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var created = Warehouse.Create(command.CompanyId, command.Code, command.Name, command.StockAccountCode);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        if ((await stock.ListWarehousesAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).Any(w => w.Code == created.Value.Code))
        {
            return Error.Conflict($"Warehouse {created.Value.Code} already exists.");
        }

        created.Value.CreatedAt = clock.GetUtcNow();
        created.Value.CreatedBy = currentUser.Username;
        stock.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(created.Value);
    }

    public async Task<Result<ItemStockSettingDto>> Handle(GetItemStockSettingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var item = await AccessibleItemAsync(query.ItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Item", query.ItemId);
        }

        var setting = (await stock.SettingsAsync(item.CompanyId, cancellationToken).ConfigureAwait(false)).GetValueOrDefault(item.Id);
        return new ItemStockSettingDto(item.Id, (setting?.Method ?? ValuationMethod.MovingAverage).ToString(),
            await stock.HasMovementsAsync(item.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<ItemStockSettingDto>> Handle(SetItemValuationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var item = await AccessibleItemAsync(command.ItemId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Item", command.ItemId);
        }

        var hasMovements = await stock.HasMovementsAsync(item.Id, cancellationToken).ConfigureAwait(false);
        var setting = (await stock.SettingsAsync(item.CompanyId, cancellationToken).ConfigureAwait(false)).GetValueOrDefault(item.Id);
        try
        {
            if (setting is null)
            {
                if (hasMovements && command.Method != ValuationMethod.MovingAverage)
                {
                    throw new DomainException("stock.method_fixed", "The valuation method cannot change once the item has stock movements.");
                }

                setting = StockItemSetting.Create(item.CompanyId, item.Id, command.Method);
                setting.CreatedAt = clock.GetUtcNow();
                stock.Add(setting);
            }
            else
            {
                setting.Change(command.Method, hasMovements);
                setting.ModifiedAt = clock.GetUtcNow();
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ItemStockSettingDto(item.Id, setting.Method.ToString(), hasMovements);
    }

    public async Task<Result<StockEntryDto>> Handle(CreateStockEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var catalogue = (await items.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(i => i.Id);
        var warehouses = (await stock.ListWarehousesAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id);
        var lines = new List<StockEntryLine>();
        foreach (var l in command.Lines ?? [])
        {
            if (!catalogue.TryGetValue(l.ItemId, out var item) || !item.IsActive)
            {
                return Error.Validation("Every line needs an active item of this company.");
            }

            if (!item.IsStockItem)
            {
                return Error.Validation($"{item.Code} is not a stock item.");
            }

            if (l.SourceWarehouseId is { } s && !warehouses.ContainsKey(s) || l.TargetWarehouseId is { } t && !warehouses.ContainsKey(t))
            {
                return Error.Validation($"{item.Code}: the warehouse is not one of this company's warehouses.");
            }

            lines.Add(new StockEntryLine(item.Id, item.Code, item.Name, item.Uom, l.Qty, l.Rate, l.SourceWarehouseId, l.TargetWarehouseId));
        }

        StockEntry entry;
        try
        {
            entry = StockEntry.Create(command.CompanyId, command.Purpose, command.PostingDate, lines, command.CounterAccountCode, command.Remark);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        entry.AssignName(await numbers.NextAsync(command.CompanyId, entry.DocumentType, DefaultPattern, command.PostingDate, cancellationToken).ConfigureAwait(false));
        entry.CreatedAt = clock.GetUtcNow();
        entry.CreatedBy = currentUser.Username;
        stock.Add(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, warehouses);
    }

    /// <summary>
    /// Writes the stock ledger through the valuation engine from the stock on the posting date, requests the value journal,
    /// then reposts every later movement (a backdated entry), all in one transaction.
    /// </summary>
    public async Task<Result<StockEntryDto>> Handle(SubmitStockEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entry = await AccessibleEntryAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Error.NotFound("Stock entry", command.Id);
        }

        var warehouses = (await stock.ListWarehousesAsync(entry.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id);
        var pairs = Pairs(entry);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await stock.LockAsync(pairs, cancellationToken).ConfigureAwait(false);
        var settings = await stock.SettingsAsync(entry.CompanyId, cancellationToken).ConfigureAwait(false);
        var positions = new Dictionary<(Guid, Guid), StockPosition>();
        foreach (var pair in pairs)
        {
            var before = (await stock.RowsForPairAsync(pair.Item, pair.Warehouse, cancellationToken).ConfigureAwait(false)).LastOrDefault(r => r.PostingDate <= entry.PostingDate);
            positions[pair] = new StockPosition(before?.State ?? ValuationState.Empty, before?.Method);
        }

        var methods = entry.Lines.Select(l => l.ItemId).Distinct()
            .ToDictionary(id => id, id => settings.GetValueOrDefault(id)?.Method ?? ValuationMethod.MovingAverage);

        StockPostingResult posted;
        try
        {
            posted = StockPosting.Post(entry, positions, methods, warehouses, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
            entry.Submit();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        foreach (var itemId in methods.Keys.Where(id => !settings.ContainsKey(id)))
        {
            stock.Add(StockItemSetting.Create(entry.CompanyId, itemId, methods[itemId])); // the method is fixed from the first movement on
        }

        stock.Add(posted.Rows);
        entry.ModifiedAt = clock.GetUtcNow();
        entry.ModifiedBy = currentUser.Username;
        if (posted.JournalLines.Count > 0)
        {
            unitOfWork.Publish(new StockJournalRequested(entry.Id, entry.Name!, entry.CompanyId, entry.PostingDate,
                $"Stock {entry.Purpose.ToString().ToLowerInvariant()} {entry.Name}{(entry.Remark is null ? "" : ": " + entry.Remark)}",
                posted.JournalLines.Select(j => new StockJournalLine(j.AccountCode, j.Debit, j.Credit)).ToList(), entry.ModifiedAt.Value));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false); // gives the new rows their sequence
        try
        {
            // Every later movement of each item and warehouse is repriced from the new rows on.
            var seeds = posted.Rows.GroupBy(r => (r.ItemId, r.WarehouseId)).Select(g => ((g.Key.ItemId, g.Key.WarehouseId), Key(g.Max(r => r.Sequence), g.First().PostingDate)));
            await RepostAsync(entry, seeds, warehouses, settings, methods, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message); // the transaction is rolled back
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, warehouses);
    }

    /// <summary>
    /// Cancels an entry at any point in history: its rows leave the ledger, its value is taken back out of the general
    /// ledger, and later movements are repriced. Refused if that would take stock below zero at any point.
    /// </summary>
    public async Task<Result<StockEntryDto>> Handle(CancelStockEntryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var entry = await AccessibleEntryAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Error.NotFound("Stock entry", command.Id);
        }

        var warehouses = (await stock.ListWarehousesAsync(entry.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id);
        var pairs = Pairs(entry);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await stock.LockAsync(pairs, cancellationToken).ConfigureAwait(false);
        try
        {
            entry.Cancel();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        var rows = await stock.RowsForVoucherAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        var signed = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            row.MarkCancelled();
            Add(signed, warehouses[row.WarehouseId].StockAccountCode, -row.ValueChange);
        }

        if (entry.Purpose != StockEntryPurpose.Transfer)
        {
            Add(signed, entry.CounterAccountCode, rows.Sum(r => r.ValueChange));
        }

        entry.ModifiedAt = clock.GetUtcNow();
        entry.ModifiedBy = currentUser.Username;
        PublishAdjustment(entry, signed, $"Cancelled {entry.Name}");
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await stock.SettingsAsync(entry.CompanyId, cancellationToken).ConfigureAwait(false);
            var seeds = rows.GroupBy(r => (r.ItemId, r.WarehouseId)).Select(g => ((g.Key.ItemId, g.Key.WarehouseId), Key(g.Min(r => r.Sequence) - 1, g.First().PostingDate)));
            await RepostAsync(entry, seeds, warehouses, settings, null, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, warehouses);
    }

    private static (DateOnly Date, long Sequence) Key(long sequence, DateOnly date) => (date, sequence);

    private static bool After(StockLedgerEntry row, (DateOnly Date, long Sequence) key) =>
        row.PostingDate > key.Date || (row.PostingDate == key.Date && row.Sequence > key.Sequence);

    private static void Add(Dictionary<string, decimal> signed, string account, decimal amount) => signed[account] = signed.GetValueOrDefault(account) + amount;

    /// <summary>
    /// Replays each seeded item and warehouse after its key; a transfer out whose value changed carries the new value to its
    /// transfer in, whose warehouse is replayed in turn. Changed issues and transfers get their entry line revalued, and
    /// each changed entry gets an adjustment journal for the difference.
    /// </summary>
    private async Task RepostAsync(StockEntry trigger, IEnumerable<((Guid Item, Guid Warehouse) Pair, (DateOnly Date, long Sequence) After)> seeds,
        Dictionary<Guid, Warehouse> warehouses, IReadOnlyDictionary<Guid, StockItemSetting> settings, IReadOnlyDictionary<Guid, ValuationMethod>? methods,
        CancellationToken cancellationToken)
    {
        var work = new Queue<((Guid Item, Guid Warehouse) Pair, (DateOnly Date, long Sequence) After)>(seeds);
        var rowsByPair = new Dictionary<(Guid, Guid), List<StockLedgerEntry>>();
        var transferValues = new Dictionary<(Guid Voucher, int Line), decimal>();
        var changed = new Dictionary<Guid, (StockLedgerEntry Row, decimal Old)>();
        var locked = new HashSet<(Guid, Guid)>(Pairs(trigger));
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

            if (!rowsByPair.TryGetValue(pair, out var rows))
            {
                rows = await stock.RowsForPairAsync(pair.Item, pair.Warehouse, cancellationToken).ConfigureAwait(false);
                rowsByPair[pair] = rows;
            }

            var index = rows.FindIndex(r => After(r, after));
            if (index < 0)
            {
                continue;
            }

            var method = methods?.GetValueOrDefault(pair.Item) ?? settings.GetValueOrDefault(pair.Item)?.Method ?? rows[index].Method;
            var start = index > 0 ? rows[index - 1].State : ValuationState.Empty;
            var replayed = Reposting.Replay(start, rows.Skip(index).ToList(), method,
                r => transferValues.TryGetValue((r.VoucherId, r.LineIndex), out var v) ? v : null);
            foreach (var (row, old) in replayed)
            {
                changed.TryAdd(row.Id, (row, old));
                if (row.Kind != MovementKind.TransferOut)
                {
                    continue;
                }

                transferValues[(row.VoucherId, row.LineIndex)] = -row.ValueChange;
                var voucher = (await stock.GetEntriesAsync([row.VoucherId], cancellationToken).ConfigureAwait(false)).Single();
                var target = (row.ItemId, voucher.Lines[row.LineIndex].TargetWarehouseId!.Value);
                if (!rowsByPair.TryGetValue(target, out var targetRows))
                {
                    if (locked.Add(target))
                    {
                        await stock.LockAsync([target], cancellationToken).ConfigureAwait(false);
                    }

                    targetRows = await stock.RowsForPairAsync(target.Item1, target.Item2, cancellationToken).ConfigureAwait(false);
                    rowsByPair[target] = targetRows;
                }

                var transferIn = targetRows.Single(r => r.VoucherId == row.VoucherId && r.LineIndex == row.LineIndex && r.Kind == MovementKind.TransferIn);
                work.Enqueue((target, (transferIn.PostingDate, transferIn.Sequence - 1)));
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
            }

            if (voucher.Purpose == StockEntryPurpose.Issue)
            {
                Add(signed, voucher.CounterAccountCode, -group.Sum(c => c.Row.ValueChange - c.Old));
            }

            voucher.ModifiedAt = clock.GetUtcNow();
            PublishAdjustment(voucher, signed, $"Stock revaluation of {voucher.Name} after {trigger.Name}");
        }
    }

    private void PublishAdjustment(StockEntry voucher, Dictionary<string, decimal> signed, string remark)
    {
        var lines = signed.Where(kv => kv.Value != 0m).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new StockJournalLine(kv.Key, Math.Max(kv.Value, 0m), Math.Max(-kv.Value, 0m))).ToList();
        if (lines.Count > 0)
        {
            unitOfWork.Publish(new StockJournalAdjusted(Guid.CreateVersion7(), voucher.Id, voucher.Name!, voucher.CompanyId, voucher.PostingDate, remark, lines, clock.GetUtcNow()));
        }
    }

    public async Task<Result<StockEntryDto>> Handle(GetStockEntryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entry = await AccessibleEntryAsync(query.Id, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return Error.NotFound("Stock entry", query.Id);
        }

        return ToDto(entry, (await stock.ListWarehousesAsync(entry.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id));
    }

    public async Task<Result<IReadOnlyList<StockEntryDto>>> Handle(ListStockEntriesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var warehouses = (await stock.ListWarehousesAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id);
        return Result.Success<IReadOnlyList<StockEntryDto>>((await stock.ListEntriesAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false))
            .Select(e => ToDto(e, warehouses)).ToList());
    }

    public async Task<Result<StockBalanceDto>> Handle(StockBalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var catalogue = (await items.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(i => i.Id);
        var warehouses = (await stock.ListWarehousesAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id);
        var rows = (await stock.BalancesAsync(query.CompanyId, query.AsOf, cancellationToken).ConfigureAwait(false))
            .Where(r => r.QtyAfter != 0m && catalogue.ContainsKey(r.ItemId) && warehouses.ContainsKey(r.WarehouseId))
            .Select(r =>
            {
                var item = catalogue[r.ItemId];
                return new StockBalanceRow(item.Id, item.Code, item.Name, item.Uom, r.WarehouseId, warehouses[r.WarehouseId].Code, r.QtyAfter,
                    decimal.Round(r.State.Rate, 4), r.ValueAfter, r.Method.ToString());
            })
            .OrderBy(r => r.ItemCode, StringComparer.Ordinal).ThenBy(r => r.Warehouse, StringComparer.Ordinal).ToList();
        return new StockBalanceDto(query.CompanyId, query.AsOf, rows, rows.Sum(r => r.Value));
    }

    public async Task<Result<StockLedgerDto>> Handle(StockLedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var item = await AccessibleItemAsync(query.ItemId, cancellationToken).ConfigureAwait(false);
        if (item is null || item.CompanyId != query.CompanyId)
        {
            return Error.NotFound("Item", query.ItemId);
        }

        var warehouses = (await stock.ListWarehousesAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id);
        var rows = (await stock.LedgerAsync(query.CompanyId, item.Id, query.WarehouseId, query.From, query.To, cancellationToken).ConfigureAwait(false))
            .Select(r => new StockLedgerRow(r.PostingDate, r.VoucherNumber, warehouses.GetValueOrDefault(r.WarehouseId)?.Code ?? "?", r.ActualQty, decimal.Round(r.Rate, 4),
                r.ValueChange, r.QtyAfter, r.ValueAfter)).ToList();
        return new StockLedgerDto(item.Id, item.Code, item.Name, rows);
    }

    private static List<(Guid Item, Guid Warehouse)> Pairs(StockEntry entry) =>
        entry.Lines.SelectMany(l => new[] { l.SourceWarehouseId, l.TargetWarehouseId }.Where(w => w is not null).Select(w => (l.ItemId, w!.Value))).Distinct().ToList();

    private async Task<ItemInfo?> AccessibleItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await items.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return item is not null && await access.CanAccessAsync(item.CompanyId, cancellationToken).ConfigureAwait(false) ? item : null;
    }

    private async Task<StockEntry?> AccessibleEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await stock.GetEntryAsync(id, cancellationToken).ConfigureAwait(false);
        return entry is not null && await access.CanAccessAsync(entry.CompanyId, cancellationToken).ConfigureAwait(false) ? entry : null;
    }

    private static WarehouseDto ToDto(Warehouse w) => new(w.Id, w.CompanyId, w.Code, w.Name, w.StockAccountCode);

    private static StockEntryDto ToDto(StockEntry e, IReadOnlyDictionary<Guid, Warehouse> warehouses) => new(e.Id, e.Name, e.CompanyId, e.Purpose.ToString(), e.PostingDate,
        e.DocStatus.ToString(), e.CounterAccountCode, e.Remark,
        e.Lines.Select(l => new StockEntryLineDto(l.ItemId, l.ItemCode, l.ItemName, l.Uom, l.Qty, l.Rate, l.SourceWarehouseId,
            l.SourceWarehouseId is { } s ? warehouses.GetValueOrDefault(s)?.Code : null, l.TargetWarehouseId,
            l.TargetWarehouseId is { } t ? warehouses.GetValueOrDefault(t)?.Code : null, l.Value)).ToList(),
        e.TotalValue);
}
