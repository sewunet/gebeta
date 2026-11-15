using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Banking;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

public sealed record ListBankAccountsQuery(Guid CompanyId) : IQuery<IReadOnlyList<BankAccountDto>>;

/// <param name="LedgerAccountCode">An existing asset account; when empty a new one is made under 1100 (1121, 1122, …).</param>
public sealed record CreateBankAccountCommand(Guid CompanyId, string Name, string BankName, string AccountNumber, string? LedgerAccountCode) : ICommand<BankAccountDto>;

public sealed record StatementLineInput(DateOnly Date, string Description, string? Reference, decimal Amount, decimal? Balance);

public sealed record ImportStatementCommand(Guid BankAccountId, IReadOnlyList<StatementLineInput> Lines) : ICommand<ImportResultDto>;

public sealed record ReconciliationQuery(Guid BankAccountId, DateOnly From, DateOnly To) : IQuery<ReconciliationDto>;

public sealed record MatchStatementLineCommand(Guid BankAccountId, Guid StatementLineId, IReadOnlyList<Guid> GlEntryIds) : ICommand<ReconciliationDto>;

public sealed record UnmatchStatementLineCommand(Guid BankAccountId, Guid StatementLineId) : ICommand<int>;

/// <summary>
/// Books an unmatched statement line the books do not have yet (bank charges, interest, a standing order) against
/// <paramref name="AccountCode"/> and matches it. Defaults: 6400 Bank Charges for money out, 4300 Interest Income for money in.
/// </summary>
public sealed record PostStatementLineCommand(Guid BankAccountId, Guid StatementLineId, string? AccountCode, string? Remark) : ICommand<JournalEntryDto>;

public sealed record AutoMatchCommand(Guid BankAccountId, DateOnly From, DateOnly To) : ICommand<int>;

public sealed class BankingHandler(
    IBankRepository banks,
    IAccountRepository accounts,
    IJournalRepository journals,
    ChartSetup chart,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IAccountingNumbers numbers,
    IAccountingUnitOfWork unitOfWork,
    TimeProvider clock)
    : IQueryHandler<ListBankAccountsQuery, IReadOnlyList<BankAccountDto>>,
      ICommandHandler<PostStatementLineCommand, JournalEntryDto>,
      ICommandHandler<CreateBankAccountCommand, BankAccountDto>,
      ICommandHandler<ImportStatementCommand, ImportResultDto>,
      IQueryHandler<ReconciliationQuery, ReconciliationDto>,
      ICommandHandler<MatchStatementLineCommand, ReconciliationDto>,
      ICommandHandler<UnmatchStatementLineCommand, int>,
      ICommandHandler<AutoMatchCommand, int>
{
    public async Task<Result<IReadOnlyList<BankAccountDto>>> Handle(ListBankAccountsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var totals = (await journals.TotalsAsync(query.CompanyId, today, cancellationToken).ConfigureAwait(false)).ToDictionary(t => t.AccountId);
        return Result.Success<IReadOnlyList<BankAccountDto>>((await banks.ListAccountsAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
            .Select(b => ToDto(b, totals.TryGetValue(b.LedgerAccountId, out var t) ? t.Debit - t.Credit : 0m)).ToList());
    }

    public async Task<Result<BankAccountDto>> Handle(CreateBankAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        if (await chart.EnsureAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var chartAccounts = await accounts.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        var existing = await banks.ListAccountsAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        Account ledger;
        if (!string.IsNullOrWhiteSpace(command.LedgerAccountCode))
        {
            var found = chartAccounts.FirstOrDefault(a => a.Code == command.LedgerAccountCode.Trim());
            if (found is null)
            {
                return Error.Validation($"Account {command.LedgerAccountCode} does not exist in this company's chart.");
            }

            if (existing.Any(b => b.LedgerAccountId == found.Id))
            {
                return Error.Conflict($"Account {found.Code} already belongs to another bank account.");
            }

            ledger = found;
        }
        else
        {
            var parent = chartAccounts.FirstOrDefault(a => a.Code == "1100");
            var used = chartAccounts.Select(a => a.Code).ToHashSet(StringComparer.Ordinal);
            var code = Enumerable.Range(1121, 79).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).FirstOrDefault(c => !used.Contains(c));
            if (parent is null || code is null)
            {
                return Error.Validation("No free account code under 1100 Cash and Bank; choose an existing account.");
            }

            var created = Account.Create(command.CompanyId, code, $"Bank - {command.BankName?.Trim()} {command.Name?.Trim()}".Trim(), RootType.Asset, false, parent.Id);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            ledger = created.Value;
            ledger.CreatedAt = clock.GetUtcNow();
            ledger.CreatedBy = currentUser.Username;
            accounts.Add(ledger);
        }

        var bank = BankAccount.Create(command.CompanyId, command.Name ?? string.Empty, command.BankName ?? string.Empty, command.AccountNumber ?? string.Empty, ledger);
        if (bank.IsFailure)
        {
            return bank.Error!;
        }

        bank.Value.CreatedAt = clock.GetUtcNow();
        bank.Value.CreatedBy = currentUser.Username;
        banks.Add(bank.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(bank.Value, 0m);
    }

    public async Task<Result<ImportResultDto>> Handle(ImportStatementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var bank = await AccessibleAsync(command.BankAccountId, cancellationToken).ConfigureAwait(false);
        if (bank is null)
        {
            return Error.NotFound("Bank account", command.BankAccountId);
        }

        var input = (command.Lines ?? []).Where(l => l.Amount != 0m).ToList();
        if (input.Count == 0)
        {
            return Error.Validation("The statement has no lines with an amount.");
        }

        var known = (await banks.LinesAsync(bank.Id, input.Min(l => l.Date), input.Max(l => l.Date), cancellationToken).ConfigureAwait(false)).ToList();
        var batch = Guid.CreateVersion7();
        var added = new List<BankStatementLine>();
        foreach (var l in input)
        {
            if (known.Any(k => k.SameAs(l.Date, l.Amount, l.Reference, l.Description)))
            {
                continue;
            }

            var line = BankStatementLine.Create(bank, l.Date, l.Description, l.Reference, l.Amount, l.Balance, batch);
            line.CreatedAt = clock.GetUtcNow();
            line.CreatedBy = currentUser.Username;
            added.Add(line);
            known.Add(line);
        }

        banks.Add(added);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new ImportResultDto(added.Count, input.Count - added.Count);
    }

    public async Task<Result<ReconciliationDto>> Handle(ReconciliationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var bank = await AccessibleAsync(query.BankAccountId, cancellationToken).ConfigureAwait(false);
        return bank is null ? Error.NotFound("Bank account", query.BankAccountId) : await BuildAsync(bank, query.From, query.To, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ReconciliationDto>> Handle(MatchStatementLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var bank = await AccessibleAsync(command.BankAccountId, cancellationToken).ConfigureAwait(false);
        var line = await banks.GetLineAsync(command.StatementLineId, cancellationToken).ConfigureAwait(false);
        if (bank is null || line is null || line.BankAccountId != bank.Id)
        {
            return Error.NotFound("Statement line", command.StatementLineId);
        }

        var matches = await banks.MatchesAsync(bank.Id, cancellationToken).ConfigureAwait(false);
        if (matches.Any(m => m.StatementLineId == line.Id))
        {
            return Error.Conflict("This statement line is already matched; unmatch it first.");
        }

        var ids = (command.GlEntryIds ?? []).Distinct().ToHashSet();
        var entries = (await journals.EntriesAsync(bank.CompanyId, bank.LedgerAccountId, DateOnly.MinValue, DateOnly.MaxValue, cancellationToken).ConfigureAwait(false))
            .Where(e => ids.Contains(e.Id)).ToList();
        if (entries.Count != ids.Count)
        {
            return Error.Validation($"Some entries are not on the bank's ledger account {bank.LedgerAccountCode}.");
        }

        if (entries.FirstOrDefault(e => matches.Any(m => m.GlEntryId == e.Id)) is { } taken)
        {
            return Error.Conflict($"{taken.VoucherNumber} is already matched to another statement line.");
        }

        try
        {
            banks.Add(BankMatch.Match(line, entries, currentUser.Username ?? "system", clock.GetUtcNow()));
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        var from = new[] { line.Date, entries.Min(e => e.PostingDate) }.Min();
        return await BuildAsync(bank, from, new[] { line.Date, entries.Max(e => e.PostingDate) }.Max(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<int>> Handle(UnmatchStatementLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var bank = await AccessibleAsync(command.BankAccountId, cancellationToken).ConfigureAwait(false);
        if (bank is null)
        {
            return Error.NotFound("Bank account", command.BankAccountId);
        }

        var matches = (await banks.MatchesAsync(bank.Id, cancellationToken).ConfigureAwait(false)).Where(m => m.StatementLineId == command.StatementLineId).ToList();
        if (matches.Count == 0)
        {
            return Error.NotFound("Match", command.StatementLineId);
        }

        banks.Remove(matches);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return matches.Count;
    }

    public async Task<Result<JournalEntryDto>> Handle(PostStatementLineCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var bank = await AccessibleAsync(command.BankAccountId, cancellationToken).ConfigureAwait(false);
        var line = await banks.GetLineAsync(command.StatementLineId, cancellationToken).ConfigureAwait(false);
        if (bank is null || line is null || line.BankAccountId != bank.Id)
        {
            return Error.NotFound("Statement line", command.StatementLineId);
        }

        if ((await banks.MatchesForLineAsync(line.Id, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return Error.Conflict("This statement line is already matched.");
        }

        if (await journals.FindBySourceAsync(line.Id, cancellationToken).ConfigureAwait(false) is { } booked)
        {
            return Error.Conflict(booked.DocStatus == DocStatus.Submitted
                ? $"This statement line is already booked by {booked.Name}."
                : $"This line was booked by {booked.Name}, which was cancelled; post a journal by hand and match it instead.");
        }

        if (await chart.EnsureAsync(bank.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var byCode = (await accounts.ListAsync(bank.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code);
        var code = string.IsNullOrWhiteSpace(command.AccountCode)
            ? (line.Amount < 0m ? EthiopianChartOfAccounts.BankCharges : EthiopianChartOfAccounts.InterestIncome)
            : command.AccountCode.Trim();
        if (!byCode.TryGetValue(code, out var other) || other.IsGroup || other.Id == bank.LedgerAccountId)
        {
            return Error.Validation($"Account {code} is not a postable account for this line.");
        }

        var amount = Math.Abs(line.Amount);
        var remark = string.IsNullOrWhiteSpace(command.Remark) ? $"{line.Description} ({bank.BankName})" : command.Remark.Trim();
        JournalLine[] lines = line.Amount < 0m
            ? [new(other.Id, amount, 0m, remark), new(bank.LedgerAccountId, 0m, amount, remark)]
            : [new(bank.LedgerAccountId, amount, 0m, remark), new(other.Id, 0m, amount, remark)];
        var journal = JournalEntry.Create(bank.CompanyId, line.Date, remark, lines, JournalSource.Bank, line.Id, line.Reference ?? bank.Name);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        journal.AssignName(await numbers.NextAsync(bank.CompanyId, journal.DocumentType, JournalHandler.DefaultPattern, line.Date, cancellationToken).ConfigureAwait(false));
        journal.CreatedAt = clock.GetUtcNow();
        journal.CreatedBy = currentUser.Username;
        var posted = journal.Post();
        journals.Add(journal);
        journals.Add(posted);
        banks.Add(BankMatch.Match(line, posted.Where(e => e.AccountId == bank.LedgerAccountId).ToList(), currentUser.Username ?? "system", clock.GetUtcNow()));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return JournalHandler.ToDto(journal, byCode.Values.ToDictionary(a => a.Id));
    }

    /// <summary>Matches every unambiguous pair (same amount, within three days, one candidate on each side).</summary>
    public async Task<Result<int>> Handle(AutoMatchCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var bank = await AccessibleAsync(command.BankAccountId, cancellationToken).ConfigureAwait(false);
        if (bank is null)
        {
            return Error.NotFound("Bank account", command.BankAccountId);
        }

        var (lines, entries, _) = await UnmatchedAsync(bank, command.From, command.To, cancellationToken).ConfigureAwait(false);
        var pairs = BankMatching.Suggest(lines, entries);
        foreach (var (line, entry) in pairs)
        {
            banks.Add(BankMatch.Match(line, [entry], currentUser.Username ?? "system", clock.GetUtcNow()));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return pairs.Count;
    }

    /// <summary>
    /// Unmatched statement lines and ledger rows in a period. Ledger rows of a voucher that was reversed on the same
    /// account (a cancelled payment, say) cancel out and are left out, unless one of them is already matched.
    /// </summary>
    private async Task<(List<BankStatementLine> Lines, List<GlEntry> Entries, IReadOnlyList<BankMatch> Matches)> UnmatchedAsync(BankAccount bank, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        var matches = await banks.MatchesAsync(bank.Id, cancellationToken).ConfigureAwait(false);
        var matchedLines = matches.Select(m => m.StatementLineId).ToHashSet();
        var matchedEntries = matches.Select(m => m.GlEntryId).ToHashSet();
        var lines = (await banks.LinesAsync(bank.Id, from, to, cancellationToken).ConfigureAwait(false)).Where(l => !matchedLines.Contains(l.Id)).ToList();
        var entries = Relevant(await journals.EntriesAsync(bank.CompanyId, bank.LedgerAccountId, from, to, cancellationToken).ConfigureAwait(false), matchedEntries)
            .Where(e => !matchedEntries.Contains(e.Id)).ToList();
        return (lines, entries, matches);
    }

    private static IEnumerable<GlEntry> Relevant(IReadOnlyList<GlEntry> entries, HashSet<Guid> matched) =>
        entries.GroupBy(e => e.VoucherId)
            .Where(g => g.Sum(BankMatch.BankAmount) != 0m || g.Any(e => matched.Contains(e.Id)))
            .SelectMany(g => g);

    private async Task<ReconciliationDto> BuildAsync(BankAccount bank, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var matches = await banks.MatchesAsync(bank.Id, cancellationToken).ConfigureAwait(false);
        var matchedEntries = matches.Select(m => m.GlEntryId).ToHashSet();
        var allEntries = await journals.EntriesAsync(bank.CompanyId, bank.LedgerAccountId, DateOnly.MinValue, to, cancellationToken).ConfigureAwait(false);
        var allLines = await banks.LinesAsync(bank.Id, DateOnly.MinValue, to, cancellationToken).ConfigureAwait(false);
        var voucherOf = allEntries.ToDictionary(e => e.Id, e => e.VoucherNumber);
        var byLine = matches.GroupBy(m => m.StatementLineId).ToDictionary(g => g.Key, g => g.Select(m => voucherOf.GetValueOrDefault(m.GlEntryId) ?? "?").Distinct().ToList());

        var relevant = Relevant(allEntries, matchedEntries).ToList();
        var lineDtos = allLines.Where(l => l.Date >= from)
            .Select(l => new StatementLineDto(l.Id, l.Date, l.Description, l.Reference, l.Amount, l.Balance, byLine.ContainsKey(l.Id),
                byLine.TryGetValue(l.Id, out var v) ? v : [])).ToList();
        var bookDtos = relevant.Where(e => e.PostingDate >= from)
            .Select(e => new BookEntryDto(e.Id, e.PostingDate, e.VoucherNumber, e.Description, BankMatch.BankAmount(e), matchedEntries.Contains(e.Id))).ToList();

        var unmatchedLines = allLines.Where(l => !byLine.ContainsKey(l.Id)).ToList();
        var unmatchedEntries = relevant.Where(e => !matchedEntries.Contains(e.Id)).ToList();
        var suggestions = BankMatching.Suggest(unmatchedLines.Where(l => l.Date >= from).ToList(), unmatchedEntries.Where(e => e.PostingDate >= from).ToList())
            .Select(p => new MatchSuggestionDto(p.Line.Id, p.Entry.Id)).ToList();

        var book = allEntries.Sum(BankMatch.BankAmount);
        var unmatchedStatement = unmatchedLines.Sum(l => l.Amount);
        var unmatchedBook = unmatchedEntries.Sum(BankMatch.BankAmount);
        var expected = book + unmatchedStatement - unmatchedBook;
        var statementBalance = allLines.LastOrDefault(l => l.Balance is not null)?.Balance;
        return new ReconciliationDto(bank.Id, from, to, lineDtos, bookDtos, suggestions, book, unmatchedStatement, unmatchedBook, expected, statementBalance,
            statementBalance is { } s ? s - expected : null);
    }

    private async Task<BankAccount?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var bank = await banks.GetAccountAsync(id, cancellationToken).ConfigureAwait(false);
        return bank is not null && await access.CanAccessAsync(bank.CompanyId, cancellationToken).ConfigureAwait(false) ? bank : null;
    }

    private static BankAccountDto ToDto(BankAccount b, decimal balance) => new(b.Id, b.CompanyId, b.Name, b.BankName, b.AccountNumber, b.LedgerAccountCode, balance);
}
