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

    /// <summary>Writes the stock ledger through the valuation engine and requests the value journal, in one transaction.</summary>
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
        var latest = await stock.LatestAsync(pairs, cancellationToken).ConfigureAwait(false);
        var settings = await stock.SettingsAsync(entry.CompanyId, cancellationToken).ConfigureAwait(false);
        var positions = pairs.ToDictionary(p => p, p => latest.TryGetValue(p, out var row)
            ? new StockPosition(row.State, row.PostingDate, row.Method)
            : new StockPosition(ValuationState.Empty, null, null));
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

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, warehouses);
    }

    /// <summary>Cancels while the entry is still the latest movement of each of its items and warehouses; later movements would need reposting.</summary>
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

        var latest = await stock.LatestAsync(pairs, cancellationToken).ConfigureAwait(false);
        if (latest.Values.FirstOrDefault(r => r.VoucherId != entry.Id) is { } later)
        {
            return Error.Validation($"{later.VoucherNumber} moved the same stock after this entry; cancel it first.");
        }

        foreach (var row in await stock.RowsForVoucherAsync(entry.Id, cancellationToken).ConfigureAwait(false))
        {
            row.MarkCancelled();
        }

        entry.ModifiedAt = clock.GetUtcNow();
        entry.ModifiedBy = currentUser.Username;
        unitOfWork.Publish(new StockEntryCancelled(entry.Id, entry.Name!, entry.CompanyId, entry.ModifiedAt.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(entry, warehouses);
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
