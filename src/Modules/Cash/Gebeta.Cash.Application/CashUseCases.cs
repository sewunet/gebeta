using Gebeta.Application;
using Gebeta.Cash.Contracts;
using Gebeta.Cash.Domain.Cashiers;
using Gebeta.SharedKernel;

namespace Gebeta.Cash.Application;

public interface ICashUnitOfWork : IUnitOfWork;

public interface ICashNumbers : IDocumentNumbers;

public sealed record UpdateCashSettingsCommand(Guid CompanyId, bool EnableAutoRequest, decimal MinimumBalance, decimal TargetBalance,
    string? DefaultCashAccountCode, string? DefaultSourceAccountCode) : ICommand<CashSettingsDto>;

public sealed record CreateCashierCommand(Guid CompanyId, string Code, string Name, CashierType Type, string? CashAccountCode, Guid? EmployeeId,
    bool AutoTopUp, decimal? MaxTransaction, decimal? MaxWithdrawal, decimal? MaxDeposit) : ICommand<CashierDto>;

public sealed record ListCashiersQuery(Guid CompanyId) : IQuery<IReadOnlyList<CashierDto>>;

public sealed record CashierLedgerQuery(Guid CashierId) : IQuery<IReadOnlyList<CashLedgerEntryDto>>;

public enum CashTransactionKind
{
    Receipt,
    Payment,
}

/// <param name="CounterAccountCode">The other side: e.g. 4100 Sales for a receipt, 6900 Other Expenses for a payment.</param>
public sealed record CashTransactionCommand(Guid CashierId, CashTransactionKind Kind, decimal Amount, string CounterAccountCode, string Description, DateOnly Date)
    : ICommand<CashMovementDto>;

public sealed record CashTransferCommand(Guid FromCashierId, Guid ToCashierId, decimal Amount, DateOnly Date, string? Description) : ICommand<CashMovementDto>;

public sealed record RequestPettyCashCommand(Guid CashierId, decimal? Amount, string? Remarks) : ICommand<PettyCashRequestDto>;

public sealed record ApprovePettyCashCommand(Guid RequestId, FundingFrom FundingFrom, string? SourceAccountCode, Guid? FundingCashierId) : ICommand<PettyCashRequestDto>;

public sealed record RejectPettyCashCommand(Guid RequestId, string Reason) : ICommand<PettyCashRequestDto>;

public sealed record ListPettyCashQuery(Guid CompanyId) : IQuery<IReadOnlyList<PettyCashRequestDto>>;

public sealed class CashHandler(
    ICashRepository cash,
    ICompanyAccess access,
    ICurrentUser currentUser,
    ICashNumbers numbers,
    ICashUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<UpdateCashSettingsCommand, CashSettingsDto>,
      ICommandHandler<CreateCashierCommand, CashierDto>,
      IQueryHandler<ListCashiersQuery, IReadOnlyList<CashierDto>>,
      IQueryHandler<CashierLedgerQuery, IReadOnlyList<CashLedgerEntryDto>>,
      ICommandHandler<CashTransactionCommand, CashMovementDto>,
      ICommandHandler<CashTransferCommand, CashMovementDto>,
      ICommandHandler<RequestPettyCashCommand, PettyCashRequestDto>,
      ICommandHandler<ApprovePettyCashCommand, PettyCashRequestDto>,
      ICommandHandler<RejectPettyCashCommand, PettyCashRequestDto>,
      IQueryHandler<ListPettyCashQuery, IReadOnlyList<PettyCashRequestDto>>
{
    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<Result<CashSettingsDto>> Handle(UpdateCashSettingsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var settings = await cash.GetSettingsAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            settings = CashSettings.Create(command.CompanyId);
            settings.CreatedAt = clock.GetUtcNow();
            cash.Add(settings);
        }

        var updated = settings.Update(command.EnableAutoRequest, command.MinimumBalance, command.TargetBalance,
            command.DefaultCashAccountCode ?? string.Empty, command.DefaultSourceAccountCode ?? string.Empty);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(settings);
    }

    public async Task<Result<CashierDto>> Handle(CreateCashierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var settings = await cash.GetSettingsAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        var created = Cashier.Create(command.CompanyId, command.Code, command.Name, command.Type,
            command.CashAccountCode ?? settings?.DefaultCashAccountCode ?? "1110", command.EmployeeId, command.AutoTopUp,
            command.MaxTransaction, command.MaxWithdrawal, command.MaxDeposit);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        if (await cash.CashierCodeExistsAsync(command.CompanyId, created.Value.Code, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"Cashier {created.Value.Code} already exists.");
        }

        created.Value.CreatedAt = clock.GetUtcNow();
        cash.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(created.Value);
    }

    public async Task<Result<IReadOnlyList<CashierDto>>> Handle(ListCashiersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<CashierDto>>((await cash.ListCashiersAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<CashLedgerEntryDto>>> Handle(CashierLedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var cashier = await AccessibleCashierAsync(query.CashierId, cancellationToken).ConfigureAwait(false);
        if (cashier is null)
        {
            return Error.NotFound("Cashier", query.CashierId);
        }

        return Result.Success<IReadOnlyList<CashLedgerEntryDto>>((await cash.LedgerAsync(cashier.Id, cancellationToken).ConfigureAwait(false))
            .Select(e => new CashLedgerEntryDto(e.Date, e.Kind.ToString(), e.SourceNumber, e.Description, e.Amount, e.BalanceAfter)).ToList());
    }

    public async Task<Result<CashMovementDto>> Handle(CashTransactionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var cashier = await AccessibleCashierAsync(command.CashierId, cancellationToken).ConfigureAwait(false);
        if (cashier is null)
        {
            return Error.NotFound("Cashier", command.CashierId);
        }

        if (string.IsNullOrWhiteSpace(command.CounterAccountCode))
        {
            return Error.Validation("Choose the other account (for example 4100 Sales or 6900 Other Expenses).");
        }

        var isReceipt = command.Kind == CashTransactionKind.Receipt;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sourceId = Guid.CreateVersion7();
        var number = await numbers.NextAsync(cashier.CompanyId, isReceipt ? "Cash Receipt" : "Cash Payment", isReceipt ? "CR-.FY.-.#####" : "CP-.FY.-.#####",
            command.Date, cancellationToken).ConfigureAwait(false);

        CashLedgerEntry entry;
        try
        {
            entry = isReceipt
                ? cashier.Receive(command.Amount, command.Date, CashMovementKind.Receipt, sourceId, number, command.Description, isDeposit: true)
                : cashier.Pay(command.Amount, command.Date, CashMovementKind.Payment, sourceId, number, command.Description);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        cash.Add(entry);
        var line = $"{cashier.Name}: {command.Description}";
        unitOfWork.Publish(new JournalRequested(sourceId, number, isReceipt ? "Cash Receipt" : "Cash Payment", cashier.CompanyId, command.Date, line,
            isReceipt
                ? [new(cashier.CashAccountCode, command.Amount, 0m, line), new(command.CounterAccountCode.Trim(), 0m, command.Amount, line)]
                : [new(command.CounterAccountCode.Trim(), command.Amount, 0m, line), new(cashier.CashAccountCode, 0m, command.Amount, line)],
            clock.GetUtcNow()));

        var topUp = isReceipt ? null : await AutoTopUpAsync(cashier, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CashMovementDto(number, cashier.Id, command.Kind.ToString(), command.Amount, cashier.Balance, topUp is null ? null : ToDto(topUp));
    }

    /// <summary>
    /// Transfer between two cashiers: both cashier ledgers move, and the journal has one line per cashier even
    /// when they share a cash account (the current app had to work around ERPNext merging such lines).
    /// </summary>
    public async Task<Result<CashMovementDto>> Handle(CashTransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var from = await AccessibleCashierAsync(command.FromCashierId, cancellationToken).ConfigureAwait(false);
        var to = await AccessibleCashierAsync(command.ToCashierId, cancellationToken).ConfigureAwait(false);
        if (from is null || to is null || from.CompanyId != to.CompanyId || from.Id == to.Id)
        {
            return Error.Validation("Choose two different cashiers of the same company.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var sourceId = Guid.CreateVersion7();
        var number = await numbers.NextAsync(from.CompanyId, "Cash Transfer", "CT-.FY.-.#####", command.Date, cancellationToken).ConfigureAwait(false);
        var description = command.Description ?? $"Transfer {from.Name} → {to.Name}";
        try
        {
            cash.Add(from.Pay(command.Amount, command.Date, CashMovementKind.TransferOut, sourceId, number, description));
            cash.Add(to.Receive(command.Amount, command.Date, CashMovementKind.TransferIn, sourceId, number, description, isDeposit: true));
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        unitOfWork.Publish(new JournalRequested(sourceId, number, "Cash Transfer", from.CompanyId, command.Date, description,
            [new(to.CashAccountCode, command.Amount, 0m, $"In: {to.Name} ({to.Code})"), new(from.CashAccountCode, 0m, command.Amount, $"Out: {from.Name} ({from.Code})")],
            clock.GetUtcNow()));
        var topUp = await AutoTopUpAsync(from, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new CashMovementDto(number, from.Id, "Transfer", command.Amount, from.Balance, topUp is null ? null : ToDto(topUp));
    }

    public async Task<Result<PettyCashRequestDto>> Handle(RequestPettyCashCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var cashier = await AccessibleCashierAsync(command.CashierId, cancellationToken).ConfigureAwait(false);
        if (cashier is null)
        {
            return Error.NotFound("Cashier", command.CashierId);
        }

        if (await cash.HasOpenRequestAsync(cashier.Id, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"{cashier.Name} already has an open petty cash request.");
        }

        var settings = await cash.GetSettingsAsync(cashier.CompanyId, cancellationToken).ConfigureAwait(false);
        var amount = command.Amount ?? (settings is null ? 0m : settings.TargetBalance - cashier.Balance);
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        PettyCashRequest request;
        try
        {
            request = await CreateRequestAsync(cashier, amount, bySystem: false, command.Remarks, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(request);
    }

    public async Task<Result<PettyCashRequestDto>> Handle(ApprovePettyCashCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = await cash.GetRequestAsync(command.RequestId, cancellationToken).ConfigureAwait(false);
        if (request is null || !await access.CanAccessAsync(request.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Petty cash request", command.RequestId);
        }

        var cashier = await cash.GetCashierAsync(request.CashierId, cancellationToken).ConfigureAwait(false);
        var funding = command.FundingCashierId is { } fid ? await cash.GetCashierAsync(fid, cancellationToken).ConfigureAwait(false) : null;
        if (command.FundingFrom == FundingFrom.Cashier && (funding is null || funding.CompanyId != request.CompanyId))
        {
            return Error.Validation("The funding cashier must be an active cashier of the same company.");
        }

        var settings = await cash.GetSettingsAsync(request.CompanyId, cancellationToken).ConfigureAwait(false);
        var sourceAccount = command.SourceAccountCode ?? settings?.DefaultSourceAccountCode;
        var date = Today;
        try
        {
            request.MarkApproved(command.FundingFrom, sourceAccount, command.FundingCashierId, currentUser.Username ?? "unknown");
            var description = $"Petty cash {request.Name} for {cashier!.Name}";
            if (funding is not null && command.FundingFrom == FundingFrom.Cashier)
            {
                cash.Add(funding.Pay(request.RequestedAmount, date, CashMovementKind.TransferOut, request.Id, request.Name!, description));
            }

            cash.Add(cashier.Receive(request.RequestedAmount, date, CashMovementKind.TopUp, request.Id, request.Name!, description, isDeposit: false));
            var credit = funding is not null && command.FundingFrom == FundingFrom.Cashier
                ? new JournalRequestLine(funding.CashAccountCode, 0m, request.RequestedAmount, $"Out: {funding.Name} ({funding.Code})")
                : new JournalRequestLine(request.SourceAccountCode!, 0m, request.RequestedAmount, description);
            unitOfWork.Publish(new JournalRequested(request.Id, request.Name!, "Petty Cash Request", request.CompanyId, date, description,
                [new(cashier.CashAccountCode, request.RequestedAmount, 0m, $"In: {cashier.Name} ({cashier.Code})"), credit], clock.GetUtcNow()));
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        request.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(request);
    }

    public async Task<Result<PettyCashRequestDto>> Handle(RejectPettyCashCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = await cash.GetRequestAsync(command.RequestId, cancellationToken).ConfigureAwait(false);
        if (request is null || !await access.CanAccessAsync(request.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Petty cash request", command.RequestId);
        }

        try
        {
            request.Reject(command.Reason);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        request.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(request);
    }

    public async Task<Result<IReadOnlyList<PettyCashRequestDto>>> Handle(ListPettyCashQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<PettyCashRequestDto>>((await cash.ListRequestsAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    /// <summary>After money goes out of a petty cashier: request a top-up to the target balance if it fell below the minimum.</summary>
    private async Task<PettyCashRequest?> AutoTopUpAsync(Cashier cashier, CancellationToken cancellationToken)
    {
        var settings = await cash.GetSettingsAsync(cashier.CompanyId, cancellationToken).ConfigureAwait(false);
        if (settings?.TopUpFor(cashier, cashier.Balance) is not { } amount
            || await cash.HasOpenRequestAsync(cashier.Id, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await CreateRequestAsync(cashier, amount, bySystem: true, "Automatic top-up: balance below the minimum", cancellationToken).ConfigureAwait(false);
    }

    private async Task<PettyCashRequest> CreateRequestAsync(Cashier cashier, decimal amount, bool bySystem, string? remarks, CancellationToken cancellationToken)
    {
        var request = PettyCashRequest.Create(cashier, amount, Today, bySystem, remarks);
        request.AssignName(await numbers.NextAsync(cashier.CompanyId, request.DocumentType, "PCR-.FY.-.#####", Today, cancellationToken).ConfigureAwait(false));
        request.SubmitForApproval();
        request.CreatedAt = clock.GetUtcNow();
        request.CreatedBy = bySystem ? "system" : currentUser.Username;
        cash.Add(request);
        return request;
    }

    private async Task<Cashier?> AccessibleCashierAsync(Guid id, CancellationToken cancellationToken)
    {
        var cashier = await cash.GetCashierAsync(id, cancellationToken).ConfigureAwait(false);
        return cashier is not null && await access.CanAccessAsync(cashier.CompanyId, cancellationToken).ConfigureAwait(false) ? cashier : null;
    }

    private static CashierDto ToDto(Cashier c) => new(c.Id, c.CompanyId, c.Code, c.Name, c.Type.ToString(), c.Status.ToString(), c.CashAccountCode, c.AutoTopUp,
        c.MaxTransaction, c.MaxWithdrawal, c.MaxDeposit, c.Balance);

    private static CashSettingsDto ToDto(CashSettings s) => new(s.CompanyId, s.EnableAutoRequest, s.MinimumBalance, s.TargetBalance, s.DefaultCashAccountCode, s.DefaultSourceAccountCode);

    private static PettyCashRequestDto ToDto(PettyCashRequest r) => new(r.Id, r.Name, r.CashierId, r.CashierName, r.RequestedAmount, r.BalanceWhenRequested,
        r.CreatedBySystem, r.Status.ToString(), r.FundingFrom?.ToString(), r.SourceAccountCode, r.FundingCashierId, r.ApprovedBy, r.RejectionReason);
}
