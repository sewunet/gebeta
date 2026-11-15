using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Domain.Ledger;

public enum RootType
{
    Asset,
    Liability,
    Equity,
    Income,
    Expense,
}

/// <summary>An account of a company's chart of accounts. Only leaf accounts (not groups) can be posted to.</summary>
public sealed class Account : Entity
{
    private Account()
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public RootType RootType { get; private set; }
    public bool IsGroup { get; private set; }
    public Guid? ParentId { get; private set; }

    /// <summary>Assets and expenses grow with debits; liabilities, equity and income with credits.</summary>
    public bool IsDebitNormal => RootType is RootType.Asset or RootType.Expense;

    public static Result<Account> Create(Guid companyId, string code, string name, RootType rootType, bool isGroup, Guid? parentId)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 20 || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("An account needs a code (at most 20 characters) and a name.");
        }

        return new Account { CompanyId = companyId, Code = code.Trim(), Name = name.Trim(), RootType = rootType, IsGroup = isGroup, ParentId = parentId };
    }
}

/// <summary>One line of the chart template.</summary>
public sealed record AccountTemplate(string Code, string Name, RootType RootType, bool IsGroup, string? ParentCode);

/// <summary>
/// A starting chart of accounts for an Ethiopian company (IFRS-style, docs/ethiopia.md), with the payroll
/// accounts Ethiopian law requires: employment income tax payable and pension contributions payable.
/// Companies extend it with their own accounts.
/// </summary>
public static class EthiopianChartOfAccounts
{
    public const string CashOnHand = "1110";
    public const string Bank = "1120";
    public const string SalaryPayable = "2210";
    public const string IncomeTaxPayable = "2220";
    public const string PensionPayable = "2230";
    public const string OtherPayrollDeductions = "2240";
    public const string SalaryExpense = "6110";
    public const string EmployerPensionExpense = "6120";
    public const string RetainedEarnings = "3200";

    public static IReadOnlyList<AccountTemplate> Accounts { get; } =
    [
        new("1000", "Assets", RootType.Asset, true, null),
        new("1100", "Cash and Bank", RootType.Asset, true, "1000"),
        new(CashOnHand, "Cash on Hand", RootType.Asset, false, "1100"),
        new(Bank, "Bank Accounts", RootType.Asset, false, "1100"),
        new("1200", "Accounts Receivable", RootType.Asset, false, "1000"),
        new("1250", "VAT Receivable (Input VAT)", RootType.Asset, false, "1000"),
        new("1260", "Withholding Tax Receivable", RootType.Asset, false, "1000"),
        new("1300", "Inventories", RootType.Asset, false, "1000"),
        new("1400", "Employee Advances", RootType.Asset, false, "1000"),
        new("1500", "Property, Plant and Equipment", RootType.Asset, false, "1000"),
        new("2000", "Liabilities", RootType.Liability, true, null),
        new("2100", "Accounts Payable", RootType.Liability, false, "2000"),
        new("2200", "Payroll Liabilities", RootType.Liability, true, "2000"),
        new(SalaryPayable, "Salaries Payable", RootType.Liability, false, "2200"),
        new(IncomeTaxPayable, "Employment Income Tax Payable", RootType.Liability, false, "2200"),
        new(PensionPayable, "Pension Contributions Payable", RootType.Liability, false, "2200"),
        new(OtherPayrollDeductions, "Other Payroll Deductions Payable", RootType.Liability, false, "2200"),
        new("2300", "VAT Payable", RootType.Liability, false, "2000"),
        new("2400", "Withholding Tax Payable", RootType.Liability, false, "2000"),
        new("3000", "Equity", RootType.Equity, true, null),
        new("3100", "Capital", RootType.Equity, false, "3000"),
        new(RetainedEarnings, "Retained Earnings", RootType.Equity, false, "3000"),
        new("4000", "Income", RootType.Income, true, null),
        new("4100", "Sales", RootType.Income, false, "4000"),
        new("4200", "Other Income", RootType.Income, false, "4000"),
        new("5000", "Cost of Sales", RootType.Expense, false, null),
        new("6000", "Operating Expenses", RootType.Expense, true, null),
        new("6100", "Employee Costs", RootType.Expense, true, "6000"),
        new(SalaryExpense, "Salaries and Wages", RootType.Expense, false, "6100"),
        new(EmployerPensionExpense, "Employer Pension Contribution", RootType.Expense, false, "6100"),
        new("6200", "Rent", RootType.Expense, false, "6000"),
        new("6300", "Utilities", RootType.Expense, false, "6000"),
        new("6900", "Other Expenses", RootType.Expense, false, "6000"),
    ];
}

/// <summary>A line of a journal entry: either a debit or a credit, never both.</summary>
public sealed record JournalLine(Guid AccountId, decimal Debit, decimal Credit, string? Description);

public enum JournalSource
{
    Manual,
    Payroll,
    Cash,
    SalesInvoice,
    PurchaseInvoice,
    Payment,

    /// <summary>Balances brought in when a company starts its books in Gebeta.</summary>
    Opening,

    /// <summary>Year-end closing: income and expenses of the fiscal year moved to retained earnings.</summary>
    YearEnd,
}

/// <summary>
/// A journal entry. Submitting posts it to the general ledger; cancelling posts the reversal
/// (append-only ledger, ADR 0004).
/// </summary>
public sealed class JournalEntry : DocumentBase
{
    private List<JournalLine> _lines = [];

    private JournalEntry()
    {
        Remark = string.Empty;
    }

    private JournalEntry(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        Remark = string.Empty;
    }

    public override string DocumentType => "Journal Entry";

    public string Remark { get; private set; }
    public JournalSource Source { get; private set; }

    /// <summary>For journals made by another module: the source document (e.g. the salary slip id).</summary>
    public Guid? SourceId { get; private set; }

    public string? SourceNumber { get; private set; }
    public IReadOnlyList<JournalLine> Lines => _lines;
    public decimal TotalDebit => _lines.Sum(l => l.Debit);
    public decimal TotalCredit => _lines.Sum(l => l.Credit);

    public static JournalEntry Create(Guid companyId, DateOnly postingDate, string remark, IEnumerable<JournalLine> lines,
        JournalSource source = JournalSource.Manual, Guid? sourceId = null, string? sourceNumber = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var list = lines.Where(l => l.Debit != 0m || l.Credit != 0m)
            .Select(l => l with { Debit = Rounding.ToCurrency(l.Debit), Credit = Rounding.ToCurrency(l.Credit) })
            .ToList();
        if (list.Count < 2)
        {
            throw new DomainException("accounting.journal_lines", "A journal entry needs at least two lines.");
        }

        if (list.Any(l => l.Debit < 0m || l.Credit < 0m || (l.Debit > 0m && l.Credit > 0m)))
        {
            throw new DomainException("accounting.journal_line_side", "Each line is either a debit or a credit, and never negative.");
        }

        return new JournalEntry(companyId, postingDate)
        {
            Remark = string.IsNullOrWhiteSpace(remark) ? "Journal entry" : remark.Trim(),
            Source = source,
            SourceId = sourceId,
            SourceNumber = sourceNumber,
            _lines = list,
        };
    }

    protected override void ValidateForSubmit()
    {
        if (TotalDebit != TotalCredit)
        {
            throw new DomainException("accounting.unbalanced", $"Debits ({TotalDebit}) and credits ({TotalCredit}) must be equal.");
        }
    }

    /// <summary>Submits and returns the ledger rows to append.</summary>
    public IReadOnlyList<GlEntry> Post()
    {
        Submit();
        return _lines.Select(l => GlEntry.Create(CompanyId, l.AccountId, PostingDate, l.Debit, l.Credit, Id, Name ?? string.Empty, l.Description ?? Remark, false)).ToList();
    }

    /// <summary>Cancels and returns the reversal rows (debits and credits swapped, same posting date).</summary>
    public IReadOnlyList<GlEntry> Reverse()
    {
        Cancel();
        return _lines.Select(l => GlEntry.Create(CompanyId, l.AccountId, PostingDate, l.Credit, l.Debit, Id, Name ?? string.Empty, $"Reversal: {l.Description ?? Remark}", true)).ToList();
    }
}

/// <summary>A row of the general ledger. Never updated or deleted.</summary>
public sealed class GlEntry : Entity
{
    private GlEntry()
    {
        VoucherNumber = string.Empty;
        Description = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public Guid AccountId { get; private set; }
    public DateOnly PostingDate { get; private set; }
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public Guid VoucherId { get; private set; }
    public string VoucherNumber { get; private set; }
    public string Description { get; private set; }
    public bool IsReversal { get; private set; }

    internal static GlEntry Create(Guid companyId, Guid accountId, DateOnly postingDate, decimal debit, decimal credit, Guid voucherId, string voucherNumber,
        string description, bool isReversal) =>
        new()
        {
            CompanyId = companyId,
            AccountId = accountId,
            PostingDate = postingDate,
            Debit = debit,
            Credit = credit,
            VoucherId = voucherId,
            VoucherNumber = voucherNumber,
            Description = description.Length > 300 ? description[..300] : description,
            IsReversal = isReversal,
        };
}

public interface IAccountRepository
{
    Task<IReadOnlyList<Account>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> AnyAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(Account account);
}

public interface IJournalRepository
{
    Task<JournalEntry?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<JournalEntry?> FindBySourceAsync(Guid sourceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JournalEntry>> ListAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    void Add(JournalEntry journal);
    void Add(IEnumerable<GlEntry> entries);

    /// <summary>Debit and credit totals per account up to and including <paramref name="asOf"/>.</summary>
    Task<IReadOnlyList<(Guid AccountId, decimal Debit, decimal Credit)>> TotalsAsync(Guid companyId, DateOnly asOf, CancellationToken cancellationToken);

    /// <summary>Debit and credit totals per account posted from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="excludeYearEnd">Leave out year-end closing journals (profit and loss shows the year's result, not its closing).</param>
    Task<IReadOnlyList<(Guid AccountId, decimal Debit, decimal Credit)>> TotalsBetweenAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken,
        bool excludeYearEnd = false);

    /// <summary>The submitted journal of a source type posted on a date, if any (e.g. the year-end closing of a fiscal year).</summary>
    Task<JournalEntry?> FindSubmittedAsync(Guid companyId, JournalSource source, DateOnly postingDate, CancellationToken cancellationToken);

    /// <summary>Ledger rows of one account in a date range, in posting order.</summary>
    Task<IReadOnlyList<GlEntry>> EntriesAsync(Guid companyId, Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
