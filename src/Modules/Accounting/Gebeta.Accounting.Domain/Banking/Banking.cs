using Gebeta.Accounting.Domain.Ledger;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Domain.Banking;

/// <summary>A company's account at a bank, with its own ledger account (a leaf under 1100 Cash and Bank).</summary>
public sealed class BankAccount : Entity
{
    private BankAccount()
    {
        Name = string.Empty;
        BankName = string.Empty;
        AccountNumber = string.Empty;
        LedgerAccountCode = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string Name { get; private set; }
    public string BankName { get; private set; }
    public string AccountNumber { get; private set; }
    public Guid LedgerAccountId { get; private set; }
    public string LedgerAccountCode { get; private set; }

    public static Result<BankAccount> Create(Guid companyId, string name, string bankName, string accountNumber, Account ledgerAccount)
    {
        ArgumentNullException.ThrowIfNull(ledgerAccount);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(accountNumber))
        {
            return Error.Validation("A bank account needs a name, the bank and the account number.");
        }

        if (ledgerAccount.CompanyId != companyId || ledgerAccount.IsGroup || ledgerAccount.RootType != RootType.Asset)
        {
            return Error.Validation($"{ledgerAccount.Code} {ledgerAccount.Name} cannot hold a bank account; choose an asset account that is not a group.");
        }

        return new BankAccount
        {
            CompanyId = companyId, Name = name.Trim(), BankName = bankName.Trim(), AccountNumber = accountNumber.Trim(),
            LedgerAccountId = ledgerAccount.Id, LedgerAccountCode = ledgerAccount.Code,
        };
    }
}

/// <summary>One line of an imported bank statement: money in (positive) or out (negative), as the bank shows it.</summary>
public sealed class BankStatementLine : Entity
{
    private BankStatementLine()
    {
        Description = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public DateOnly Date { get; private set; }
    public string Description { get; private set; }
    public string? Reference { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>The running balance printed on the statement, when the file has one.</summary>
    public decimal? Balance { get; private set; }

    public Guid ImportBatchId { get; private set; }

    public static BankStatementLine Create(BankAccount account, DateOnly date, string description, string? reference, decimal amount, decimal? balance, Guid batchId)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (amount == 0m)
        {
            throw new DomainException("accounting.statement_amount", "A statement line needs an amount.");
        }

        return new BankStatementLine
        {
            CompanyId = account.CompanyId, BankAccountId = account.Id, Date = date,
            Description = string.IsNullOrWhiteSpace(description) ? "—" : (description.Trim().Length > 300 ? description.Trim()[..300] : description.Trim()),
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(), Amount = Rounding.ToCurrency(amount),
            Balance = balance is { } b ? Rounding.ToCurrency(b) : null, ImportBatchId = batchId,
        };
    }

    /// <summary>The same line imported twice (same day, amount and reference or description) is skipped.</summary>
    public bool SameAs(DateOnly date, decimal amount, string? reference, string description) =>
        Date == date && Amount == Rounding.ToCurrency(amount)
        && (Reference is not null && !string.IsNullOrWhiteSpace(reference)
            ? string.Equals(Reference, reference.Trim(), StringComparison.OrdinalIgnoreCase)
            : string.Equals(Description, description?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A statement line matched to ledger rows of the bank's ledger account. Removing it unmatches (nothing in the ledger changes).</summary>
public sealed class BankMatch : Entity
{
    private BankMatch()
    {
    }

    public Guid CompanyId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public Guid StatementLineId { get; private set; }
    public Guid GlEntryId { get; private set; }

    /// <summary>The ledger row's amount as the bank sees it (+ debit to the bank account, − credit).</summary>
    public decimal Amount { get; private set; }

    public static IReadOnlyList<BankMatch> Match(BankStatementLine line, IReadOnlyList<GlEntry> entries, string by, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            throw new DomainException("accounting.match_empty", "Choose at least one ledger entry.");
        }

        var total = entries.Sum(BankAmount);
        if (total != line.Amount)
        {
            throw new DomainException("accounting.match_amount", $"The ledger entries ({total}) do not add up to the statement line ({line.Amount}).");
        }

        return entries.Select(e => new BankMatch
        {
            CompanyId = line.CompanyId, BankAccountId = line.BankAccountId, StatementLineId = line.Id, GlEntryId = e.Id, Amount = BankAmount(e),
            CreatedAt = at, CreatedBy = by,
        }).ToList();
    }

    public static decimal BankAmount(GlEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Debit - entry.Credit;
    }
}

/// <summary>Matching suggestions: one statement line to one ledger row with the same amount, within a few days.</summary>
public static class BankMatching
{
    public const int DaysTolerance = 3;

    /// <summary>Pairs where the amount is the same and each side has exactly one candidate (so the match is not a guess).</summary>
    public static IReadOnlyList<(BankStatementLine Line, GlEntry Entry)> Suggest(IReadOnlyList<BankStatementLine> lines, IReadOnlyList<GlEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(entries);
        bool Near(BankStatementLine l, GlEntry e) => l.Amount == BankMatch.BankAmount(e) && Math.Abs(l.Date.DayNumber - e.PostingDate.DayNumber) <= DaysTolerance;
        var result = new List<(BankStatementLine, GlEntry)>();
        foreach (var line in lines)
        {
            var candidates = entries.Where(e => Near(line, e)).ToList();
            if (candidates.Count == 1 && lines.Count(l => Near(l, candidates[0])) == 1)
            {
                result.Add((line, candidates[0]));
            }
        }

        return result;
    }
}

public interface IBankRepository
{
    Task<IReadOnlyList<BankAccount>> ListAccountsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<BankAccount?> GetAccountAsync(Guid id, CancellationToken cancellationToken);
    void Add(BankAccount account);

    Task<IReadOnlyList<BankStatementLine>> LinesAsync(Guid bankAccountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<BankStatementLine?> GetLineAsync(Guid id, CancellationToken cancellationToken);
    void Add(IEnumerable<BankStatementLine> lines);

    Task<IReadOnlyList<BankMatch>> MatchesAsync(Guid bankAccountId, CancellationToken cancellationToken);
    void Add(IEnumerable<BankMatch> matches);
    void Remove(IEnumerable<BankMatch> matches);
}
