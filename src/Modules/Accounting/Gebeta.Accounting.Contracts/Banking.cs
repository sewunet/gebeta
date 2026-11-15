namespace Gebeta.Accounting.Contracts;

public static class BankingPermissions
{
    public const string Read = "accounting.banking.read";
    public const string Manage = "accounting.banking.manage";
}

public sealed record BankAccountDto(Guid Id, Guid CompanyId, string Name, string BankName, string AccountNumber, string LedgerAccountCode, decimal BookBalance);

public sealed record ImportResultDto(int Imported, int Duplicates);

public sealed record StatementLineDto(Guid Id, DateOnly Date, string Description, string? Reference, decimal Amount, decimal? Balance, bool Matched,
    IReadOnlyList<string> MatchedVouchers);

/// <summary>A ledger row of the bank's ledger account; <see cref="Amount"/> as the bank sees it (+ money in, − money out).</summary>
public sealed record BookEntryDto(Guid GlEntryId, DateOnly Date, string Voucher, string Description, decimal Amount, bool Matched);

public sealed record MatchSuggestionDto(Guid StatementLineId, Guid GlEntryId);

/// <summary>
/// Bank reconciliation for a period. Summary (everything up to <see cref="To"/>): book balance, plus statement lines
/// not yet in the books, less book entries not yet on the statement, should equal the statement balance.
/// </summary>
public sealed record ReconciliationDto(Guid BankAccountId, DateOnly From, DateOnly To, IReadOnlyList<StatementLineDto> StatementLines,
    IReadOnlyList<BookEntryDto> BookEntries, IReadOnlyList<MatchSuggestionDto> Suggestions, decimal BookBalance, decimal UnmatchedStatement,
    decimal UnmatchedBook, decimal ExpectedStatementBalance, decimal? StatementBalance, decimal? Difference);
