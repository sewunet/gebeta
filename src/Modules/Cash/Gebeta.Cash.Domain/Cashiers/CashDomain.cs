using Gebeta.SharedKernel;

namespace Gebeta.Cash.Domain.Cashiers;

public enum CashierType
{
    /// <summary>Collects sales cash; never topped up automatically (cash goes to the bank).</summary>
    SalesCashier,

    /// <summary>Pays small expenses; can be topped up automatically when low.</summary>
    PettyCashier,

    Other,
}

public enum CashierStatus
{
    Active,
    Suspended,
    Closed,
}

/// <summary>
/// A cashier (docs/modules/cash-management.md). The balance is the sum of the cashier's own ledger and is
/// kept on the cashier for the overdraw check; PostgreSQL's row version (xmin) stops two movements from
/// updating it at the same time.
/// </summary>
public sealed class Cashier : Entity
{
    private Cashier()
    {
        Code = string.Empty;
        Name = string.Empty;
        CashAccountCode = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public CashierType Type { get; private set; }
    public CashierStatus Status { get; private set; } = CashierStatus.Active;
    public Guid? EmployeeId { get; private set; }

    /// <summary>The general ledger cash account this cashier's money sits in (several cashiers may share one).</summary>
    public string CashAccountCode { get; private set; }

    public bool AutoTopUp { get; private set; }
    public decimal? MaxTransaction { get; private set; }
    public decimal? MaxWithdrawal { get; private set; }
    public decimal? MaxDeposit { get; private set; }
    public decimal Balance { get; private set; }

    public static Result<Cashier> Create(Guid companyId, string code, string name, CashierType type, string cashAccountCode, Guid? employeeId,
        bool autoTopUp, decimal? maxTransaction, decimal? maxWithdrawal, decimal? maxDeposit)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 20 || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("A cashier needs a code (at most 20 characters) and a name.");
        }

        if (string.IsNullOrWhiteSpace(cashAccountCode))
        {
            return Error.Validation("A cashier needs a cash account.");
        }

        if (new[] { maxTransaction, maxWithdrawal, maxDeposit }.Any(l => l is <= 0m))
        {
            return Error.Validation("Limits must be positive when set.");
        }

        return new Cashier
        {
            CompanyId = companyId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Type = type,
            CashAccountCode = cashAccountCode.Trim(),
            EmployeeId = employeeId,
            AutoTopUp = autoTopUp && type == CashierType.PettyCashier,
            MaxTransaction = maxTransaction,
            MaxWithdrawal = maxWithdrawal,
            MaxDeposit = maxDeposit,
        };
    }

    /// <summary>Money coming in (receipt, top-up, transfer in).</summary>
    public CashLedgerEntry Receive(decimal amount, DateOnly date, CashMovementKind kind, Guid sourceId, string sourceNumber, string description, bool isDeposit)
    {
        EnsureActive();
        EnsurePositive(amount);
        if (MaxTransaction is { } maxT && amount > maxT && kind == CashMovementKind.Receipt)
        {
            throw new DomainException("cash.limit_transaction", $"{Name} may take at most {maxT} in one transaction.");
        }

        if (isDeposit && MaxDeposit is { } maxD && amount > maxD)
        {
            throw new DomainException("cash.limit_deposit", $"{Name} may receive at most {maxD} at a time.");
        }

        Balance += amount;
        return CashLedgerEntry.Create(CompanyId, Id, date, amount, kind, sourceId, sourceNumber, description, Balance);
    }

    /// <summary>Money going out. The cashier's own balance may never go below zero.</summary>
    public CashLedgerEntry Pay(decimal amount, DateOnly date, CashMovementKind kind, Guid sourceId, string sourceNumber, string description)
    {
        EnsureActive();
        EnsurePositive(amount);
        if (MaxTransaction is { } maxT && amount > maxT && kind == CashMovementKind.Payment)
        {
            throw new DomainException("cash.limit_transaction", $"{Name} may pay at most {maxT} in one transaction.");
        }

        if (kind == CashMovementKind.TransferOut && MaxWithdrawal is { } maxW && amount > maxW)
        {
            throw new DomainException("cash.limit_withdrawal", $"{Name} may hand over at most {maxW} at a time.");
        }

        if (amount > Balance)
        {
            throw new DomainException("cash.overdraw", $"This would overdraw {Name} ({Code}): balance {Balance}, payment {amount}.");
        }

        Balance -= amount;
        return CashLedgerEntry.Create(CompanyId, Id, date, -amount, kind, sourceId, sourceNumber, description, Balance);
    }

    public void Suspend() => Status = CashierStatus.Suspended;

    public void Activate() => Status = CashierStatus.Active;

    private void EnsureActive()
    {
        if (Status != CashierStatus.Active)
        {
            throw new DomainException("cash.cashier_inactive", $"{Name} is {Status}.");
        }
    }

    private static void EnsurePositive(decimal amount)
    {
        if (amount <= 0m || decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("cash.amount", "Amounts are positive, with at most 2 decimals (santim).");
        }
    }
}

public enum CashMovementKind
{
    Receipt,
    Payment,
    TransferIn,
    TransferOut,
    TopUp,
}

/// <summary>A row of a cashier's ledger. Append-only; <see cref="BalanceAfter"/> is the running balance.</summary>
public sealed class CashLedgerEntry : Entity
{
    private CashLedgerEntry()
    {
        SourceNumber = string.Empty;
        Description = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public Guid CashierId { get; private set; }
    public DateOnly Date { get; private set; }

    /// <summary>Positive for money in, negative for money out.</summary>
    public decimal Amount { get; private set; }

    public CashMovementKind Kind { get; private set; }
    public Guid SourceId { get; private set; }
    public string SourceNumber { get; private set; }
    public string Description { get; private set; }
    public decimal BalanceAfter { get; private set; }

    internal static CashLedgerEntry Create(Guid companyId, Guid cashierId, DateOnly date, decimal amount, CashMovementKind kind, Guid sourceId,
        string sourceNumber, string description, decimal balanceAfter) =>
        new()
        {
            CompanyId = companyId, CashierId = cashierId, Date = date, Amount = amount, Kind = kind, SourceId = sourceId,
            SourceNumber = sourceNumber, Description = description.Length > 300 ? description[..300] : description, BalanceAfter = balanceAfter,
        };
}

/// <summary>Per-company cash settings (the current app's Cash Accounting Settings).</summary>
public sealed class CashSettings : Entity
{
    private CashSettings()
    {
        DefaultCashAccountCode = "1110";
        DefaultSourceAccountCode = "1120";
    }

    public Guid CompanyId { get; private set; }
    public bool EnableAutoRequest { get; private set; }
    public decimal MinimumBalance { get; private set; }
    public decimal TargetBalance { get; private set; }
    public string DefaultCashAccountCode { get; private set; }

    /// <summary>Where top-ups normally come from (a bank account).</summary>
    public string DefaultSourceAccountCode { get; private set; }

    public static CashSettings Create(Guid companyId) => new() { CompanyId = companyId };

    public Result Update(bool enableAutoRequest, decimal minimumBalance, decimal targetBalance, string cashAccountCode, string sourceAccountCode)
    {
        if (minimumBalance < 0m || targetBalance <= minimumBalance)
        {
            return Result.Failure(Error.Validation("The target balance must be above the minimum balance."));
        }

        EnableAutoRequest = enableAutoRequest;
        MinimumBalance = minimumBalance;
        TargetBalance = targetBalance;
        DefaultCashAccountCode = string.IsNullOrWhiteSpace(cashAccountCode) ? DefaultCashAccountCode : cashAccountCode.Trim();
        DefaultSourceAccountCode = string.IsNullOrWhiteSpace(sourceAccountCode) ? DefaultSourceAccountCode : sourceAccountCode.Trim();
        return Result.Success();
    }

    /// <summary>Whether a petty cashier at <paramref name="balance"/> needs a top-up, and how much.</summary>
    public decimal? TopUpFor(Cashier cashier, decimal balance)
    {
        ArgumentNullException.ThrowIfNull(cashier);
        return EnableAutoRequest && cashier.Type == CashierType.PettyCashier && cashier.AutoTopUp && balance < MinimumBalance
            ? TargetBalance - balance
            : null;
    }
}

public enum PettyCashStatus
{
    Draft,
    Pending,
    ApprovedAndPaid,
    Rejected,
    Cancelled,
}

public enum FundingFrom
{
    /// <summary>A bank or other non-cash account.</summary>
    SourceAccount,

    /// <summary>Another active cashier hands over the cash.</summary>
    Cashier,
}

/// <summary>A request to top up a cashier. Submitted = pending approval; approval pays it from a source account or another cashier.</summary>
public sealed class PettyCashRequest : DocumentBase
{
    private PettyCashRequest()
    {
        CashierName = string.Empty;
    }

    private PettyCashRequest(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        CashierName = string.Empty;
    }

    public override string DocumentType => "Petty Cash Request";

    public Guid CashierId { get; private set; }
    public string CashierName { get; private set; }
    public decimal RequestedAmount { get; private set; }
    public decimal BalanceWhenRequested { get; private set; }
    public bool CreatedBySystem { get; private set; }
    public PettyCashStatus Status { get; private set; } = PettyCashStatus.Draft;
    public FundingFrom? FundingFrom { get; private set; }
    public string? SourceAccountCode { get; private set; }
    public Guid? FundingCashierId { get; private set; }
    public string? ApprovedBy { get; private set; }
    public string? RejectionReason { get; private set; }
    public string? Remarks { get; private set; }

    public bool IsOpen => Status is PettyCashStatus.Draft or PettyCashStatus.Pending;

    public static PettyCashRequest Create(Cashier cashier, decimal amount, DateOnly date, bool bySystem, string? remarks)
    {
        ArgumentNullException.ThrowIfNull(cashier);
        if (amount <= 0m)
        {
            throw new DomainException("cash.request_amount", "The requested amount must be positive.");
        }

        return new PettyCashRequest(cashier.CompanyId, date)
        {
            CashierId = cashier.Id, CashierName = cashier.Name, RequestedAmount = Rounding.ToCurrency(amount), BalanceWhenRequested = cashier.Balance,
            CreatedBySystem = bySystem, Remarks = remarks,
        };
    }

    /// <summary>Sends the request for approval.</summary>
    public void SubmitForApproval()
    {
        Submit();
        Status = PettyCashStatus.Pending;
    }

    public void MarkApproved(FundingFrom fundingFrom, string? sourceAccountCode, Guid? fundingCashierId, string approvedBy)
    {
        if (Status != PettyCashStatus.Pending)
        {
            throw new DomainException("cash.request_not_pending", $"Only a pending request can be approved; this one is {Status}.");
        }

        if (fundingFrom == Cashiers.FundingFrom.SourceAccount && string.IsNullOrWhiteSpace(sourceAccountCode))
        {
            throw new DomainException("cash.request_source", "Choose the source account the money comes from.");
        }

        if (fundingFrom == Cashiers.FundingFrom.Cashier && (fundingCashierId is null || fundingCashierId == CashierId))
        {
            throw new DomainException("cash.request_funding_cashier", "Choose another cashier to hand over the cash.");
        }

        FundingFrom = fundingFrom;
        SourceAccountCode = fundingFrom == Cashiers.FundingFrom.SourceAccount ? sourceAccountCode!.Trim() : null;
        FundingCashierId = fundingFrom == Cashiers.FundingFrom.Cashier ? fundingCashierId : null;
        ApprovedBy = approvedBy;
        Status = PettyCashStatus.ApprovedAndPaid;
    }

    public void Reject(string reason)
    {
        if (Status != PettyCashStatus.Pending)
        {
            throw new DomainException("cash.request_not_pending", $"Only a pending request can be rejected; this one is {Status}.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("cash.request_reason", "A reason is required.");
        }

        RejectionReason = reason.Trim();
        Status = PettyCashStatus.Rejected;
    }
}

public interface ICashRepository
{
    Task<Cashier?> GetCashierAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Cashier>> ListCashiersAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> CashierCodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken);
    Task<IReadOnlyList<CashLedgerEntry>> LedgerAsync(Guid cashierId, CancellationToken cancellationToken);
    Task<CashSettings?> GetSettingsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<PettyCashRequest?> GetRequestAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PettyCashRequest>> ListRequestsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> HasOpenRequestAsync(Guid cashierId, CancellationToken cancellationToken);
    void Add(Cashier cashier);
    void Add(CashLedgerEntry entry);
    void Add(CashSettings settings);
    void Add(PettyCashRequest request);
}
