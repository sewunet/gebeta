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
    public const string PeriodsClose = "accounting.periods.close";

    public static IReadOnlyList<string> All { get; } = [AccountsRead, AccountsManage, JournalsRead, JournalsCreate, JournalsSubmit, JournalsCancel, ReportsRead, PeriodsClose];
}

public sealed record AccountDto(Guid Id, string Code, string Name, string RootType, bool IsGroup, Guid? ParentId);

public sealed record JournalLineDto(string AccountCode, string AccountName, decimal Debit, decimal Credit, string? Description);

public sealed record JournalEntryDto(Guid Id, string? Number, Guid CompanyId, DateOnly PostingDate, string Remark, string Source, string? SourceNumber,
    string Status, IReadOnlyList<JournalLineDto> Lines, decimal TotalDebit, decimal TotalCredit);

public sealed record TrialBalanceRow(string Code, string Name, string RootType, decimal Debit, decimal Credit, decimal Balance);

public sealed record TrialBalanceDto(Guid CompanyId, DateOnly AsOf, IReadOnlyList<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit);

public sealed record JournalPosted(Guid JournalId, string Number, Guid CompanyId, string Source, Guid? SourceId, decimal Amount, DateTimeOffset OccurredAt);

public sealed record PeriodClosingDto(Guid Id, string Label, DateOnly Start, DateOnly End, string Status, decimal TotalDebit, decimal TotalCredit, decimal NetProfit,
    DateTimeOffset ClosedAt, string ClosedBy, DateTimeOffset? ReopenedAt, string? ReopenedBy, string? ReopenReason);

/// <summary>Month closings, newest first. Nothing can be posted on or before <see cref="LockDate"/>.</summary>
public sealed record PeriodClosingsDto(Guid CompanyId, DateOnly? LockDate, IReadOnlyList<PeriodClosingDto> Closings);

/// <summary>An account's amount on its normal side (income and liabilities positive when credited).</summary>
public sealed record ReportRow(string Code, string Name, string RootType, decimal Amount);

public sealed record ProfitAndLossDto(Guid CompanyId, DateOnly From, DateOnly To, IReadOnlyList<ReportRow> Income, IReadOnlyList<ReportRow> Expenses,
    decimal TotalIncome, decimal TotalExpenses, decimal NetProfit);

/// <param name="UnclosedProfit">Income less expenses to date not yet moved to retained earnings by a year-end closing.</param>
public sealed record BalanceSheetDto(Guid CompanyId, DateOnly AsOf, IReadOnlyList<ReportRow> Assets, IReadOnlyList<ReportRow> Liabilities, IReadOnlyList<ReportRow> Equity,
    decimal UnclosedProfit, decimal TotalAssets, decimal TotalLiabilities, decimal TotalEquity, bool Balanced);

public sealed record GeneralLedgerRow(DateOnly Date, string Voucher, string Description, decimal Debit, decimal Credit, decimal Balance, bool IsReversal);

/// <summary>One account's ledger; balances are on the account's normal side.</summary>
public sealed record GeneralLedgerDto(string AccountCode, string AccountName, DateOnly From, DateOnly To, decimal Opening, IReadOnlyList<GeneralLedgerRow> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal Closing);

/// <param name="Current">Empty when the account has nothing in this column ("–" on screen, not 0.00).</param>
/// <param name="ChangePercent">Empty when the comparison amount is zero ("n/a").</param>
public sealed record ComparativeRow(string Code, string Name, decimal? Current, decimal? Comparison, decimal Change, decimal? ChangePercent);

public sealed record ComparativeSection(string Name, IReadOnlyList<ComparativeRow> Rows, ComparativeRow Total);

/// <summary>A profit and loss or balance sheet with a comparison column (balance sheet: the From and To dates are the same).</summary>
/// <param name="Note">Said instead of a column of zeros, for example when the comparison falls before the company's first entries.</param>
public sealed record ComparativeReportDto(Guid CompanyId, string Report, string Compare, DateOnly CurrentFrom, DateOnly CurrentTo, DateOnly ComparisonFrom,
    DateOnly ComparisonTo, IReadOnlyList<ComparativeSection> Sections, IReadOnlyList<ComparativeRow> Summary, bool CurrentBalanced, bool ComparisonBalanced, string? Note);

