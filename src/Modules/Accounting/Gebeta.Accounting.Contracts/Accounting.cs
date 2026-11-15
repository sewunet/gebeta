namespace Gebeta.Accounting.Contracts;

public static class AccountingPermissions
{
    public const string AccountsRead = "accounting.accounts.read";
    public const string AccountsManage = "accounting.accounts.manage";
    public const string JournalsRead = "accounting.journals.read";
    public const string JournalsCreate = "accounting.journals.create";
    public const string JournalsSubmit = "accounting.journals.submit";
    public const string JournalsCancel = "accounting.journals.cancel";
    public const string ReportsRead = "accounting.reports.read";

    public static IReadOnlyList<string> All { get; } = [AccountsRead, AccountsManage, JournalsRead, JournalsCreate, JournalsSubmit, JournalsCancel, ReportsRead];
}

public sealed record AccountDto(Guid Id, string Code, string Name, string RootType, bool IsGroup, Guid? ParentId);

public sealed record JournalLineDto(string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Description);

public sealed record JournalEntryDto(Guid Id, string? Number, Guid CompanyId, DateOnly PostingDate, string Remark, string Source, string? SourceNumber,
    string Status, IReadOnlyList<JournalLineDto> Lines, decimal TotalDebit, decimal TotalCredit);

public sealed record TrialBalanceRow(string Code, string Name, string RootType, decimal Debit, decimal Credit, decimal Balance);

public sealed record TrialBalanceDto(Guid CompanyId, DateOnly AsOf, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit);

public sealed record JournalPosted(Guid JournalId, string Number, Guid CompanyId, string Source, Guid? SourceId, decimal Amount, DateTimeOffset OccurredAt);
