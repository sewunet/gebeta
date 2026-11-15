using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

/// <summary>Closes the month containing <paramref name="Date"/> (an Ethiopian month for companies on the Ethiopian fiscal year).</summary>
public sealed record ClosePeriodCommand(Guid CompanyId, DateOnly Date) : ICommand<PeriodClosingDto>;

/// <summary>Reopens the latest closed month.</summary>
public sealed record ReopenPeriodCommand(Guid CompanyId, string Reason) : ICommand<PeriodClosingDto>;

public sealed record ListPeriodClosingsQuery(Guid CompanyId) : IQuery<PeriodClosingsDto>;

public sealed record ProfitAndLossQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<ProfitAndLossDto>;

public sealed record BalanceSheetQuery(Guid CompanyId, DateOnly AsOf) : IQuery<BalanceSheetDto>;

public sealed record GeneralLedgerQuery(Guid CompanyId, string AccountCode, DateOnly From, DateOnly To) : IQuery<GeneralLedgerDto>;

public sealed class PeriodsAndReportsHandler(
    IAccountRepository accounts,
    IJournalRepository journals,
    IInvoiceRepository invoices,
    IPeriodClosingRepository closings,
    ICompanyDirectory companies,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IAccountingUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<ClosePeriodCommand, PeriodClosingDto>,
      ICommandHandler<ReopenPeriodCommand, PeriodClosingDto>,
      IQueryHandler<ListPeriodClosingsQuery, PeriodClosingsDto>,
      IQueryHandler<ProfitAndLossQuery, ProfitAndLossDto>,
      IQueryHandler<BalanceSheetQuery, BalanceSheetDto>,
      IQueryHandler<GeneralLedgerQuery, GeneralLedgerDto>
{
    /// <summary>
    /// Month-end close: no draft journals or invoices may be left in the month, the trial balance must balance,
    /// and months close in order. The trial balance totals and the month's profit are kept with the closing.
    /// </summary>
    public async Task<Result<PeriodClosingDto>> Handle(ClosePeriodCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var company = await AccessibleCompanyAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        if (company is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var period = AccountingPeriod.Containing(command.Date, company.UsesEthiopianFiscalYear);
        var draftJournals = (await journals.ListAsync(command.CompanyId, period.Start, period.End, cancellationToken).ConfigureAwait(false))
            .Where(j => j.DocStatus == DocStatus.Draft).Select(j => j.Name).ToList();
        var draftInvoices = (await invoices.ListAsync(command.CompanyId, null, period.Start, period.End, cancellationToken).ConfigureAwait(false))
            .Where(i => i.DocStatus == DocStatus.Draft).Select(i => i.Name).ToList();
        if (draftJournals.Count + draftInvoices.Count > 0)
        {
            return Error.Validation($"{period.Label} still has drafts; submit or delete them first: {string.Join(", ", draftJournals.Concat(draftInvoices))}.");
        }

        var byId = (await accounts.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var balances = (await journals.TotalsAsync(command.CompanyId, period.End, cancellationToken).ConfigureAwait(false)).Select(t => t.Debit - t.Credit).ToList();
        var month = await journals.TotalsBetweenAsync(command.CompanyId, period.Start, period.End, cancellationToken).ConfigureAwait(false);
        var profit = -month.Where(t => byId.TryGetValue(t.AccountId, out var a) && a.RootType is RootType.Income or RootType.Expense).Sum(t => t.Debit - t.Credit);

        var latest = await closings.LatestClosedAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        PeriodClosing closing;
        try
        {
            closing = PeriodClosing.Close(command.CompanyId, period, latest?.End, balances.Where(b => b > 0m).Sum(), -balances.Where(b => b < 0m).Sum(), profit,
                currentUser.Username ?? "system", clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            return ex.Code == "accounting.period_already_closed" ? Error.Conflict(ex.Message) : Error.Validation(ex.Message);
        }

        closings.Add(closing);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(closing);
    }

    public async Task<Result<PeriodClosingDto>> Handle(ReopenPeriodCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var latest = await closings.LatestClosedAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        if (latest is null)
        {
            return Error.Validation("No month is closed.");
        }

        try
        {
            latest.Reopen(currentUser.Username ?? "system", clock.GetUtcNow(), command.Reason);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(latest);
    }

    public async Task<Result<PeriodClosingsDto>> Handle(ListPeriodClosingsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var all = await closings.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
        var lockDate = all.Where(c => c.Status == PeriodClosingStatus.Closed).Select(c => (DateOnly?)c.End).Max();
        return new PeriodClosingsDto(query.CompanyId, lockDate, all.Select(ToDto).ToList());
    }

    public async Task<Result<ProfitAndLossDto>> Handle(ProfitAndLossQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.To < query.From)
        {
            return Error.Validation("The end date is before the start date.");
        }

        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var rows = await RowsAsync(query.CompanyId, await journals.TotalsBetweenAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        var income = rows.Where(r => r.RootType == nameof(RootType.Income)).ToList();
        var expenses = rows.Where(r => r.RootType == nameof(RootType.Expense)).ToList();
        var totalIncome = income.Sum(r => r.Amount);
        var totalExpenses = expenses.Sum(r => r.Amount);
        return new ProfitAndLossDto(query.CompanyId, query.From, query.To, income, expenses, totalIncome, totalExpenses, totalIncome - totalExpenses);
    }

    /// <summary>Assets = liabilities + equity, where equity includes income less expenses not yet closed to retained earnings.</summary>
    public async Task<Result<BalanceSheetDto>> Handle(BalanceSheetQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var rows = await RowsAsync(query.CompanyId, await journals.TotalsAsync(query.CompanyId, query.AsOf, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);
        List<ReportRow> Of(RootType type) => rows.Where(r => r.RootType == type.ToString()).ToList();
        var assets = Of(RootType.Asset);
        var liabilities = Of(RootType.Liability);
        var equity = Of(RootType.Equity);
        var profit = Of(RootType.Income).Sum(r => r.Amount) - Of(RootType.Expense).Sum(r => r.Amount);
        var totalAssets = assets.Sum(r => r.Amount);
        var totalLiabilities = liabilities.Sum(r => r.Amount);
        var totalEquity = equity.Sum(r => r.Amount) + profit;
        return new BalanceSheetDto(query.CompanyId, query.AsOf, assets, liabilities, equity, profit, totalAssets, totalLiabilities, totalEquity,
            totalAssets == totalLiabilities + totalEquity);
    }

    public async Task<Result<GeneralLedgerDto>> Handle(GeneralLedgerQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.To < query.From)
        {
            return Error.Validation("The end date is before the start date.");
        }

        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var account = (await accounts.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(a => a.Code == query.AccountCode?.Trim());
        if (account is null)
        {
            return Error.NotFound("Account", query.AccountCode ?? string.Empty);
        }

        decimal Signed(decimal debit, decimal credit) => account.IsDebitNormal ? debit - credit : credit - debit;
        var before = (await journals.TotalsAsync(query.CompanyId, query.From.AddDays(-1), cancellationToken).ConfigureAwait(false))
            .Where(t => t.AccountId == account.Id).Select(t => Signed(t.Debit, t.Credit)).SingleOrDefault();
        var running = before;
        var rows = new List<GeneralLedgerRow>();
        foreach (var e in await journals.EntriesAsync(query.CompanyId, account.Id, query.From, query.To, cancellationToken).ConfigureAwait(false))
        {
            running += Signed(e.Debit, e.Credit);
            rows.Add(new GeneralLedgerRow(e.PostingDate, e.VoucherNumber, e.Description, e.Debit, e.Credit, running, e.IsReversal));
        }

        return new GeneralLedgerDto(account.Code, account.Name, query.From, query.To, before, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit), running);
    }

    private async Task<List<ReportRow>> RowsAsync(Guid companyId, IReadOnlyList<(Guid AccountId, decimal Debit, decimal Credit)> totals, CancellationToken cancellationToken)
    {
        var byId = (await accounts.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
        return totals.Where(t => byId.ContainsKey(t.AccountId) && t.Debit != t.Credit)
            .Select(t =>
            {
                var a = byId[t.AccountId];
                return new ReportRow(a.Code, a.Name, a.RootType.ToString(), a.IsDebitNormal ? t.Debit - t.Credit : t.Credit - t.Debit);
            })
            .OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    private async Task<CompanyInfo?> AccessibleCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        await access.CanAccessAsync(companyId, cancellationToken).ConfigureAwait(false)
            ? await companies.GetAsync(companyId, cancellationToken).ConfigureAwait(false)
            : null;

    private static PeriodClosingDto ToDto(PeriodClosing c) => new(c.Id, c.Label, c.Start, c.End, c.Status.ToString(), c.TotalDebit, c.TotalCredit, c.NetProfit,
        c.ClosedAt, c.ClosedBy, c.ReopenedAt, c.ReopenedBy, c.ReopenReason);
}
