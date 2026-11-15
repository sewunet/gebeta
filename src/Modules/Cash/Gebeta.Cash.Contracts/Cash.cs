namespace Gebeta.Cash.Contracts;

public static class CashPermissions
{
    public const string CashiersRead = "cash.cashiers.read";
    public const string CashiersManage = "cash.cashiers.manage";
    public const string TransactionsCreate = "cash.transactions.create";
    public const string PettyCashRequest = "cash.petty-cash.request";
    public const string PettyCashApprove = "cash.petty-cash.approve";
    public const string SettingsManage = "cash.settings.manage";

    public static IReadOnlyList<string> All { get; } = [CashiersRead, CashiersManage, TransactionsCreate, PettyCashRequest, PettyCashApprove, SettingsManage];
}

public sealed record CashierDto(Guid Id, Guid CompanyId, string Code, string Name, string Type, string Status, string CashAccountCode, bool AutoTopUp,
    decimal? MaxTransaction, decimal? MaxWithdrawal, decimal? MaxDeposit, decimal Balance);

public sealed record CashLedgerEntryDto(DateOnly Date, string Kind, string SourceNumber, string Description, decimal Amount, decimal BalanceAfter);

public sealed record CashMovementDto(string Number, Guid CashierId, string Kind, decimal Amount, decimal BalanceAfter, PettyCashRequestDto? TopUpRequest);

public sealed record CashSettingsDto(Guid CompanyId, bool EnableAutoRequest, decimal MinimumBalance, decimal TargetBalance, string DefaultCashAccountCode, string DefaultSourceAccountCode);

public sealed record PettyCashRequestDto(Guid Id, string? Number, Guid CashierId, string CashierName, decimal RequestedAmount, decimal BalanceWhenRequested,
    bool CreatedBySystem, string Status, string? FundingFrom, string? SourceAccountCode, Guid? FundingCashierId, string? ApprovedBy, string? RejectionReason);

/// <summary>One line of a journal Cash asks Accounting to post.</summary>
public sealed record JournalRequestLine(string AccountCode, decimal Debit, decimal Credit, string Description);

/// <summary>
/// Cash asks for a general ledger journal (docs/modules/accounting.md, journals from other modules). Accounting
/// posts it when installed; without Accounting nobody subscribes and the cashier ledger still has every movement.
/// </summary>
public sealed record JournalRequested(Guid SourceId, string SourceNumber, string SourceType, Guid CompanyId, DateOnly PostingDate, string Remark,
    IReadOnlyList<JournalRequestLine> Lines, DateTimeOffset OccurredAt);
