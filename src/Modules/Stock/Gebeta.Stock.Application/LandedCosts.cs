using Gebeta.Application;
using Gebeta.SharedKernel;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Domain.Ledger;

namespace Gebeta.Stock.Application;

public sealed record LandedChargeInput(string Description, decimal Amount, AllocationBasis Basis, string? AccountCode);

/// <param name="Lines">Weight and volume per receipt line, needed only when a charge is shared by them.</param>
public sealed record CreateLandedCostCommand(Guid ReceiptId, DateOnly PostingDate, IReadOnlyList<LandedChargeInput> Charges, IReadOnlyList<LandedCostLineInput>? Lines, string? Remark)
    : ICommand<LandedCostDto>;

public sealed record SubmitLandedCostCommand(Guid Id) : ICommand<LandedCostDto>;

public sealed record CancelLandedCostCommand(Guid Id) : ICommand<LandedCostDto>;

public sealed record GetLandedCostQuery(Guid Id) : IQuery<LandedCostDto>;

public sealed record ListLandedCostsQuery(Guid ReceiptId) : IQuery<IReadOnlyList<LandedCostDto>>;

/// <summary>
/// Landed costs (docs/modules/stock.md): submitting adds the charges to the value of the receipt's ledger rows, then reposts what
/// came after (later issues and transfers take the new cost; each gets its adjustment journal), and asks Accounting for the
/// journal of the charges themselves (stock account against the charge accounts). Cancelling takes them back the same way.
/// All in one transaction, under the locks of the receipt's items and warehouses.
/// </summary>
public sealed class LandedCostHandler(
    IStockRepository stock,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IStockNumbers numbers,
    IStockUnitOfWork unitOfWork,
    StockReposter reposter,
    TimeProvider clock)
    : ICommandHandler<CreateLandedCostCommand, LandedCostDto>,
      ICommandHandler<SubmitLandedCostCommand, LandedCostDto>,
      ICommandHandler<CancelLandedCostCommand, LandedCostDto>,
      IQueryHandler<GetLandedCostQuery, LandedCostDto>,
      IQueryHandler<ListLandedCostsQuery, IReadOnlyList<LandedCostDto>>
{
    public const string DefaultPattern = "LCV-.FY.-.#####";

    public async Task<Result<LandedCostDto>> Handle(CreateLandedCostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var receipt = await stock.GetEntryAsync(command.ReceiptId, cancellationToken).ConfigureAwait(false);
        if (receipt is null || !await access.CanAccessAsync(receipt.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Stock entry", command.ReceiptId);
        }

        LandedCost cost;
        try
        {
            cost = LandedCost.Create(receipt, command.PostingDate, (command.Charges ?? []).Select(c => new LandedCharge(c.Description, c.Amount, c.Basis, c.AccountCode ?? string.Empty)).ToList(),
                command.Lines, command.Remark);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        cost.AssignName(await numbers.NextAsync(cost.CompanyId, cost.DocumentType, DefaultPattern, cost.PostingDate, cancellationToken).ConfigureAwait(false));
        cost.CreatedAt = clock.GetUtcNow();
        cost.CreatedBy = currentUser.Username;
        stock.Add(cost);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(cost);
    }

    public Task<Result<LandedCostDto>> Handle(SubmitLandedCostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ApplyAsync(command.Id, submit: true, cancellationToken);
    }

    public Task<Result<LandedCostDto>> Handle(CancelLandedCostCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ApplyAsync(command.Id, submit: false, cancellationToken);
    }

    private async Task<Result<LandedCostDto>> ApplyAsync(Guid id, bool submit, CancellationToken cancellationToken)
    {
        var cost = await AccessibleAsync(id, cancellationToken).ConfigureAwait(false);
        if (cost is null)
        {
            return Error.NotFound("Landed cost", id);
        }

        var receipt = await stock.GetEntryAsync(cost.ReceiptId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Receipt of landed cost {cost.Name} is missing.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (submit && (receipt.DocStatus != DocStatus.Submitted || cost.PostingDate > today))
        {
            return Error.Validation(cost.PostingDate > today ? "A landed cost cannot be dated in the future." : $"{receipt.Name} is not a submitted receipt.");
        }

        var warehouses = (await stock.ListWarehousesAsync(cost.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(w => w.Id);
        var pairs = StockHandler.Pairs(receipt);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await stock.LockAsync(pairs, cancellationToken).ConfigureAwait(false);
        try
        {
            if (submit)
            {
                cost.Submit();
            }
            else
            {
                cost.Cancel();
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        // Receipt rows carry the line index; each line's extra cost goes onto its row (cancelling takes it off again).
        var sign = submit ? 1m : -1m;
        var rows = (await stock.RowsForVoucherAsync(receipt.Id, cancellationToken).ConfigureAwait(false)).Where(r => !r.IsCancelled && r.Kind == MovementKind.Receipt).ToList();
        var signed = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var line in cost.Lines.Where(l => l.Allocated != 0m))
        {
            var row = rows.SingleOrDefault(r => r.LineIndex == line.ReceiptLineIndex)
                ?? throw new InvalidOperationException($"{receipt.Name} line {line.ReceiptLineIndex} has no stock ledger row.");
            row.AddLandedCost(sign * line.Allocated);
            receipt.RevalueLine(line.ReceiptLineIndex, receipt.Lines[line.ReceiptLineIndex].Value + sign * line.Allocated);
            var stockAccount = warehouses[row.WarehouseId].StockAccountCode;
            signed[stockAccount] = signed.GetValueOrDefault(stockAccount) + sign * line.Allocated;
        }

        foreach (var charge in cost.Charges)
        {
            signed[charge.AccountCode] = signed.GetValueOrDefault(charge.AccountCode) - sign * charge.Amount;
        }

        var now = clock.GetUtcNow();
        cost.ModifiedAt = now;
        cost.ModifiedBy = currentUser.Username;
        receipt.ModifiedAt = now;
        reposter.PublishAdjustment(cost.Id, cost.Name!, cost.CompanyId, cost.PostingDate, signed,
            $"{(submit ? "Landed cost" : "Cancelled landed cost")} {cost.Name} on {receipt.Name}{(cost.Remark is null ? "" : ": " + cost.Remark)}");
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Everything of these items after the receipt (and the receipt's own row, now worth more or less) is replayed.
            var seeds = rows.GroupBy(r => (r.ItemId, r.WarehouseId)).Select(g => ((g.Key.ItemId, g.Key.WarehouseId), (g.First().PostingDate, g.Min(r => r.Sequence) - 1)));
            var settings = await stock.SettingsAsync(cost.CompanyId, cancellationToken).ConfigureAwait(false);
            await reposter.RepostAsync(pairs, cost.Name!, seeds, warehouses, settings, null, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message); // the transaction is rolled back
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(cost);
    }

    public async Task<Result<LandedCostDto>> Handle(GetLandedCostQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cost = await AccessibleAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return cost is null ? Error.NotFound("Landed cost", query.Id) : ToDto(cost);
    }

    public async Task<Result<IReadOnlyList<LandedCostDto>>> Handle(ListLandedCostsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var receipt = await stock.GetEntryAsync(query.ReceiptId, cancellationToken).ConfigureAwait(false);
        if (receipt is null || !await access.CanAccessAsync(receipt.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Stock entry", query.ReceiptId);
        }

        return Result.Success<IReadOnlyList<LandedCostDto>>((await stock.ListLandedCostsAsync(query.ReceiptId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    private async Task<LandedCost?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var cost = await stock.GetLandedCostAsync(id, cancellationToken).ConfigureAwait(false);
        return cost is not null && await access.CanAccessAsync(cost.CompanyId, cancellationToken).ConfigureAwait(false) ? cost : null;
    }

    private static LandedCostDto ToDto(LandedCost c) => new(c.Id, c.Name, c.CompanyId, c.ReceiptId, c.ReceiptNumber, c.PostingDate, c.DocStatus.ToString(), c.Remark,
        c.Charges.Select(x => new LandedChargeDto(x.Description, x.Amount, x.Basis.ToString(), x.AccountCode)).ToList(),
        c.Lines.Select(l => new LandedCostLineDto(l.ReceiptLineIndex, l.ItemId, l.ItemCode, l.ItemName, l.Qty, l.ReceivedValue, l.Weight, l.Volume, l.Allocated)).ToList(), c.TotalCharges);
}
