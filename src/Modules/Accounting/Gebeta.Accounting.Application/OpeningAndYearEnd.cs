using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

/// <summary>
/// Balances brought in at cut-over (docs/roadmap.md, Phase 4): the trial balance from the old system on the
/// day before the company starts in Gebeta. Posted at once; it must balance.
/// </summary>
public sealed record PostOpeningBalancesCommand(Guid CompanyId, DateOnly Date, IReadOnlyList<JournalLineInput> Lines, string? Remark) : ICommand<JournalEntryDto>;

/// <summary>Closes the fiscal year containing <paramref name="Date"/>: income and expenses go to retained earnings.</summary>
public sealed record CloseYearCommand(Guid CompanyId, DateOnly Date) : ICommand<JournalEntryDto>;

public sealed class OpeningAndYearEndHandler(
    IAccountRepository accounts,
    IJournalRepository journals,
    IPeriodClosingRepository closings,
    ChartSetup chart,
    ICompanyDirectory companies,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IAccountingNumbers numbers,
    IAccountingUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<PostOpeningBalancesCommand, JournalEntryDto>, ICommandHandler<CloseYearCommand, JournalEntryDto>
{
    public async Task<Result<JournalEntryDto>> Handle(PostOpeningBalancesCommand command, CancellationToken cancellationToken)
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

        var byCode = (await accounts.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code);
        var lines = new List<JournalLine>();
        foreach (var line in command.Lines ?? [])
        {
            if (!byCode.TryGetValue(line.AccountCode?.Trim() ?? string.Empty, out var account) || account.IsGroup)
            {
                return Error.Validation($"Account {line.AccountCode} is not a postable account of this company.");
            }

            lines.Add(new JournalLine(account.Id, line.Debit, line.Credit, line.Description ?? "Opening balance"));
        }

        var debit = lines.Sum(l => l.Debit);
        var credit = lines.Sum(l => l.Credit);
        if (debit != credit)
        {
            return Error.Validation($"Opening balances must balance: debits {debit}, credits {credit} (difference {debit - credit}).");
        }

        return await PostAsync(command.CompanyId, command.Date, string.IsNullOrWhiteSpace(command.Remark) ? "Opening balances" : command.Remark, lines,
            JournalSource.Opening, "Opening", byCode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One journal dated the last day of the fiscal year: every income and expense account is brought to zero and
    /// the difference (the year's profit or loss) goes to retained earnings (3200). The year's last month must
    /// still be open; close it afterwards. Cancelling the journal (Accounting → journals) reopens the year.
    /// </summary>
    public async Task<Result<JournalEntryDto>> Handle(CloseYearCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var company = await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)
            ? await companies.GetAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)
            : null;
        if (company is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var year = FiscalYearSpan.Containing(command.Date, company.UsesEthiopianFiscalYear);
        if (await journals.FindSubmittedAsync(command.CompanyId, JournalSource.YearEnd, year.End, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return Error.Conflict($"{year.Label} is already closed by {existing.Name}.");
        }

        if (await closings.LatestClosedAsync(command.CompanyId, cancellationToken).ConfigureAwait(false) is { } locked && locked.End >= year.End)
        {
            return Error.Validation($"The books are closed up to {locked.End:yyyy-MM-dd}; reopen {locked.Label} to post the year-end closing.");
        }

        var all = await accounts.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        var byId = all.ToDictionary(a => a.Id);
        var byCode = all.ToDictionary(a => a.Code);
        if (!byCode.TryGetValue(EthiopianChartOfAccounts.RetainedEarnings, out var retained))
        {
            return Error.Validation($"The chart has no retained earnings account ({EthiopianChartOfAccounts.RetainedEarnings}).");
        }

        var nets = (await journals.TotalsBetweenAsync(command.CompanyId, year.Start, year.End, cancellationToken, excludeYearEnd: true).ConfigureAwait(false))
            .Where(t => byId.TryGetValue(t.AccountId, out var a) && a.RootType is RootType.Income or RootType.Expense && t.Debit != t.Credit)
            .Select(t => (t.AccountId, Net: t.Debit - t.Credit))
            .ToList();
        if (nets.Count == 0)
        {
            return Error.Validation($"{year.Label} has no income or expenses to close.");
        }

        var label = $"Year-end closing {year.Label}";
        var lines = nets.OrderBy(n => byId[n.AccountId].Code, StringComparer.Ordinal)
            .Select(n => new JournalLine(n.AccountId, Math.Max(-n.Net, 0m), Math.Max(n.Net, 0m), label)).ToList();
        var profit = -nets.Sum(n => n.Net);
        if (profit != 0m)
        {
            lines.Add(new JournalLine(retained.Id, Math.Max(-profit, 0m), Math.Max(profit, 0m), profit > 0m ? $"Profit {year.Label}" : $"Loss {year.Label}"));
        }

        return await PostAsync(command.CompanyId, year.End, label, lines, JournalSource.YearEnd, year.Label, byCode, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<JournalEntryDto>> PostAsync(Guid companyId, DateOnly date, string remark, List<JournalLine> lines, JournalSource source, string sourceNumber,
        IReadOnlyDictionary<string, Account> byCode, CancellationToken cancellationToken)
    {
        JournalEntry journal;
        try
        {
            journal = JournalEntry.Create(companyId, date, remark, lines, source, null, sourceNumber);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        journal.AssignName(await numbers.NextAsync(companyId, journal.DocumentType, JournalHandler.DefaultPattern, date, cancellationToken).ConfigureAwait(false));
        journal.CreatedAt = clock.GetUtcNow();
        journal.CreatedBy = currentUser.Username;
        journals.Add(journal);
        journals.Add(journal.Post());
        unitOfWork.Publish(new JournalPosted(journal.Id, journal.Name!, companyId, source.ToString(), null, journal.TotalDebit, journal.CreatedAt));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return JournalHandler.ToDto(journal, byCode.Values.ToDictionary(a => a.Id));
    }
}
