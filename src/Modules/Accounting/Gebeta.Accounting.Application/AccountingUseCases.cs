using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

public interface IAccountingUnitOfWork : IUnitOfWork;

public interface IAccountingNumbers : IDocumentNumbers;

/// <summary>
/// Creates the company's chart of accounts from the Ethiopian template, and adds template accounts introduced
/// by later releases (e.g. input VAT) to existing charts. Accounts the company already has are not touched.
/// </summary>
public sealed class ChartSetup(IAccountRepository accounts, TimeProvider clock)
{
    public async Task<bool> EnsureAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var existing = (await accounts.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code, a => a.Id, StringComparer.Ordinal);
        var missing = EthiopianChartOfAccounts.Accounts.Where(t => !existing.ContainsKey(t.Code)).ToList();
        if (missing.Count == 0)
        {
            return false;
        }

        var ids = new Dictionary<string, Guid>(existing, StringComparer.Ordinal);
        foreach (var template in missing)
        {
            var account = Account.Create(companyId, template.Code, template.Name, template.RootType, template.IsGroup,
                template.ParentCode is null ? null : ids[template.ParentCode]).Value;
            account.CreatedAt = clock.GetUtcNow();
            account.CreatedBy = "system";
            ids[template.Code] = account.Id;
            accounts.Add(account);
        }

        return true;
    }
}

// ---------------------------------------------------------------- event consumers

/// <summary>A new company gets the Ethiopian chart of accounts.</summary>
public sealed class CompanyCreatedConsumer(ChartSetup chart, IAccountingUnitOfWork unitOfWork) : IIntegrationEventConsumer<CompanyCreated>
{
    public async Task HandleAsync(CompanyCreated integrationEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        if (await chart.EnsureAsync(integrationEvent.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Posts the journals other modules ask for (payroll, cash, stock), all the same way: a repeat of the same source is
/// ignored, the company gets its chart if it has none yet, account codes are mapped to the company's accounts (an
/// unknown or group account fails the message, which is retried and then dead-lettered), and the journal is numbered
/// and posted in the message's transaction. One place for the rules instead of one copy per module.
/// </summary>
public sealed class ModuleJournalPoster(
    ChartSetup chart,
    IAccountRepository accounts,
    IJournalRepository journals,
    IAccountingNumbers numbers,
    IAccountingUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public sealed record Line(string AccountCode, decimal Debit, decimal Credit, string? Description);

    /// <returns>The posted journal, or null when this source was already posted.</returns>
    public async Task<JournalEntry?> PostAsync(Guid companyId, Guid sourceId, string sourceNumber, JournalSource source, DateOnly postingDate, string remark,
        IReadOnlyList<Line> lines, string postedBy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (await journals.FindBySourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return null;
        }

        if (await chart.EnsureAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var byCode = (await accounts.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code);
        var journalLines = lines.Select(l => byCode.TryGetValue(l.AccountCode, out var account) && !account.IsGroup
                ? new JournalLine(account.Id, l.Debit, l.Credit, l.Description ?? remark)
                : throw new DomainException("accounting.unknown_account", $"Account {l.AccountCode} from {sourceNumber} is not a postable account."))
            .ToList();
        var journal = JournalEntry.Create(companyId, postingDate, remark, journalLines, source, sourceId, sourceNumber);
        journal.AssignName(await numbers.NextAsync(companyId, journal.DocumentType, JournalHandler.DefaultPattern, postingDate, cancellationToken).ConfigureAwait(false));
        journal.CreatedAt = clock.GetUtcNow();
        journal.CreatedBy = postedBy;
        journals.Add(journal);
        journals.Add(journal.Post());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return journal;
    }

    /// <summary>Reverses the journal posted for a source (its document was cancelled); nothing to do if there is none or it is already reversed.</summary>
    public async Task ReverseAsync(Guid sourceId, CancellationToken cancellationToken)
    {
        var journal = await journals.FindBySourceAsync(sourceId, cancellationToken).ConfigureAwait(false);
        if (journal is null || journal.DocStatus != DocStatus.Submitted)
        {
            return;
        }

        journals.Add(journal.Reverse());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// The payroll journal of a submitted salary slip (docs/modules/accounting.md): earnings to salary expense,
/// employer contributions to their expense and to pension payable, income tax and pension deductions to
/// their payables, other deductions to other payroll deductions, and net pay to salaries payable.
/// </summary>
public sealed class SalarySlipSubmittedConsumer(ModuleJournalPoster poster, IAccountingUnitOfWork unitOfWork) : IIntegrationEventConsumer<SalarySlipSubmitted>
{
    private static readonly HashSet<string> TaxComponents = ["IT", "BP_TAX"];
    private static readonly HashSet<string> PensionComponents = ["PE", "BP_DED"];

    public async Task HandleAsync(SalarySlipSubmitted slip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slip);
        var signed = new Dictionary<string, decimal>(StringComparer.Ordinal); // + debit, − credit
        void Add(string code, decimal amount) => signed[code] = signed.GetValueOrDefault(code) + amount;

        foreach (var line in slip.Lines)
        {
            switch (line.Type)
            {
                case "Earning":
                    Add(EthiopianChartOfAccounts.SalaryExpense, line.Amount);
                    break;
                case "EmployerContribution":
                    Add(EthiopianChartOfAccounts.EmployerPensionExpense, line.Amount);
                    Add(EthiopianChartOfAccounts.PensionPayable, -line.Amount);
                    break;
                case "Deduction" when TaxComponents.Contains(line.Abbreviation) || line.Component.Contains("Income Tax", StringComparison.OrdinalIgnoreCase):
                    Add(EthiopianChartOfAccounts.IncomeTaxPayable, -line.Amount);
                    break;
                case "Deduction" when PensionComponents.Contains(line.Abbreviation):
                    Add(EthiopianChartOfAccounts.PensionPayable, -line.Amount);
                    break;
                case "Deduction":
                    Add(EthiopianChartOfAccounts.OtherPayrollDeductions, -line.Amount);
                    break;
            }
        }

        Add(EthiopianChartOfAccounts.SalaryPayable, -slip.NetPay);
        var lines = signed.Where(kv => kv.Value != 0m).OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new ModuleJournalPoster.Line(kv.Key, Math.Max(kv.Value, 0m), Math.Max(-kv.Value, 0m), $"Payroll {slip.Number}")).ToList();
        var journal = await poster.PostAsync(slip.CompanyId, slip.SlipId, slip.Number, JournalSource.Payroll, slip.PeriodEnd,
            $"Payroll journal for salary slip {slip.Number}", lines, "payroll", cancellationToken).ConfigureAwait(false);
        if (journal is not null)
        {
            unitOfWork.Publish(new JournalPosted(journal.Id, journal.Name!, journal.CompanyId, journal.Source.ToString(), journal.SourceId, journal.TotalDebit, journal.CreatedAt));
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>A cancelled salary slip reverses its payroll journal.</summary>
public sealed class SalarySlipCancelledConsumer(ModuleJournalPoster poster) : IIntegrationEventConsumer<SalarySlipCancelled>
{
    public Task HandleAsync(SalarySlipCancelled slip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slip);
        return poster.ReverseAsync(slip.SlipId, cancellationToken);
    }
}

/// <summary>A journal requested by Cash Management (receipt, payment, transfer, petty cash top-up).</summary>
public sealed class CashJournalRequestedConsumer(ModuleJournalPoster poster) : IIntegrationEventConsumer<Gebeta.Cash.Contracts.JournalRequested>
{
    public Task HandleAsync(Gebeta.Cash.Contracts.JournalRequested request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return poster.PostAsync(request.CompanyId, request.SourceId, request.SourceNumber, JournalSource.Cash, request.PostingDate,
            $"{request.SourceType} {request.SourceNumber}: {request.Remark}",
            request.Lines.Select(l => new ModuleJournalPoster.Line(l.AccountCode, l.Debit, l.Credit, l.Description)).ToList(), "cash", cancellationToken);
    }
}

/// <summary>A stock entry's value journal (perpetual inventory): stock accounts against the counter account or each other.</summary>
public sealed class StockJournalRequestedConsumer(ModuleJournalPoster poster) : IIntegrationEventConsumer<Gebeta.Stock.Contracts.StockJournalRequested>
{
    public Task HandleAsync(Gebeta.Stock.Contracts.StockJournalRequested request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return poster.PostAsync(request.CompanyId, request.SourceId, request.SourceNumber, JournalSource.Stock, request.PostingDate, request.Remark,
            request.Lines.Select(l => new ModuleJournalPoster.Line(l.AccountCode, l.Debit, l.Credit, null)).ToList(), "stock", cancellationToken);
    }
}

/// <summary>
/// A stock value adjustment: reposting repriced an entry, or the entry was cancelled. Posted as its own journal (source id
/// = the adjustment), so the ledger stays append-only and a repeat does nothing.
/// </summary>
public sealed class StockJournalAdjustedConsumer(ModuleJournalPoster poster) : IIntegrationEventConsumer<Gebeta.Stock.Contracts.StockJournalAdjusted>
{
    public Task HandleAsync(Gebeta.Stock.Contracts.StockJournalAdjusted adjustment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(adjustment);
        return poster.PostAsync(adjustment.CompanyId, adjustment.AdjustmentId, adjustment.VoucherNumber, JournalSource.Stock, adjustment.PostingDate, adjustment.Remark,
            adjustment.Lines.Select(l => new ModuleJournalPoster.Line(l.AccountCode, l.Debit, l.Credit, null)).ToList(), "stock", cancellationToken);
    }
}

/// <summary>A cancelled stock entry reverses its value journal (events from before step 30; cancelling now sends an adjustment).</summary>
public sealed class StockEntryCancelledConsumer(ModuleJournalPoster poster) : IIntegrationEventConsumer<Gebeta.Stock.Contracts.StockEntryCancelled>
{
    public Task HandleAsync(Gebeta.Stock.Contracts.StockEntryCancelled cancelled, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cancelled);
        return poster.ReverseAsync(cancelled.SourceId, cancellationToken);
    }
}

// ---------------------------------------------------------------- accounts, journals, reports

public sealed record ListAccountsQuery(Guid CompanyId) : IQuery<IReadOnlyList<AccountDto>>;

public sealed record SetUpChartCommand(Guid CompanyId) : ICommand<IReadOnlyList<AccountDto>>;

/// <summary>A company's own account (a bank loan, accumulated depreciation...) under one of its groups; it takes the group's root type.</summary>
public sealed record CreateAccountCommand(Guid CompanyId, string Code, string Name, string ParentCode, bool IsGroup = false) : ICommand<AccountDto>;

public sealed record JournalLineInput(string AccountCode, decimal Debit, decimal Credit, string? Description);

public sealed record CreateJournalCommand(Guid CompanyId, DateOnly PostingDate, string Remark, IReadOnlyList<JournalLineInput> Lines) : ICommand<JournalEntryDto>;

public sealed record SubmitJournalCommand(Guid JournalId) : ICommand<JournalEntryDto>;

public sealed record CancelJournalCommand(Guid JournalId) : ICommand<JournalEntryDto>;

public sealed record GetJournalQuery(Guid JournalId) : IQuery<JournalEntryDto>;

public sealed record ListJournalsQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<JournalEntryDto>>;

public sealed record TrialBalanceQuery(Guid CompanyId, DateOnly AsOf) : IQuery<TrialBalanceDto>;

public sealed class JournalHandler(
    IAccountRepository accounts,
    IJournalRepository journals,
    Gebeta.Accounting.Domain.Banking.IBankRepository banks,
    ChartSetup chart,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IAccountingNumbers numbers,
    IAccountingUnitOfWork unitOfWork,
    TimeProvider clock)
    : IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountDto>>,
      ICommandHandler<SetUpChartCommand, IReadOnlyList<AccountDto>>,
      ICommandHandler<CreateAccountCommand, AccountDto>,
      ICommandHandler<CreateJournalCommand, JournalEntryDto>,
      ICommandHandler<SubmitJournalCommand, JournalEntryDto>,
      ICommandHandler<CancelJournalCommand, JournalEntryDto>,
      IQueryHandler<GetJournalQuery, JournalEntryDto>,
      IQueryHandler<ListJournalsQuery, IReadOnlyList<JournalEntryDto>>,
      IQueryHandler<TrialBalanceQuery, TrialBalanceDto>
{
    public const string DefaultPattern = "JV-.FY.-.#####";

    public async Task<Result<IReadOnlyList<AccountDto>>> Handle(ListAccountsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<AccountDto>>((await accounts.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<AccountDto>>> Handle(SetUpChartCommand command, CancellationToken cancellationToken)
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

        return await Handle(new ListAccountsQuery(command.CompanyId), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<AccountDto>> Handle(CreateAccountCommand command, CancellationToken cancellationToken)
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

        var all = await accounts.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false);
        var parent = all.FirstOrDefault(a => a.Code == command.ParentCode?.Trim());
        if (parent is null || !parent.IsGroup)
        {
            return Error.Validation($"{command.ParentCode} is not a group of this company's chart; a new account goes under a group such as 2000 Liabilities.");
        }

        if (all.Any(a => a.Code == command.Code?.Trim()))
        {
            return Error.Conflict($"The chart already has account {command.Code?.Trim()}.");
        }

        var created = Account.Create(command.CompanyId, command.Code ?? string.Empty, command.Name ?? string.Empty, parent.RootType, command.IsGroup, parent.Id);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        accounts.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(created.Value);
    }

    public async Task<Result<JournalEntryDto>> Handle(CreateJournalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var byCode = (await accounts.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code);
        var lines = new List<JournalLine>();
        foreach (var line in command.Lines ?? [])
        {
            if (!byCode.TryGetValue(line.AccountCode, out var account))
            {
                return Error.Validation($"Account {line.AccountCode} does not exist in this company's chart of accounts.");
            }

            if (account.IsGroup)
            {
                return Error.Validation($"{account.Code} {account.Name} is a group; post to one of its accounts.");
            }

            lines.Add(new JournalLine(account.Id, line.Debit, line.Credit, line.Description));
        }

        JournalEntry journal;
        try
        {
            journal = JournalEntry.Create(command.CompanyId, command.PostingDate, command.Remark, lines);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        journal.AssignName(await numbers.NextAsync(command.CompanyId, journal.DocumentType, DefaultPattern, command.PostingDate, cancellationToken).ConfigureAwait(false));
        journal.CreatedAt = clock.GetUtcNow();
        journal.CreatedBy = currentUser.Username;
        journals.Add(journal);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(journal, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<JournalEntryDto>> Handle(SubmitJournalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var journal = await AccessibleAsync(command.JournalId, cancellationToken).ConfigureAwait(false);
        if (journal is null)
        {
            return Error.NotFound("Journal entry", command.JournalId);
        }

        try
        {
            journals.Add(journal.Post());
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        journal.ModifiedAt = clock.GetUtcNow();
        unitOfWork.Publish(new JournalPosted(journal.Id, journal.Name!, journal.CompanyId, journal.Source.ToString(), journal.SourceId, journal.TotalDebit, journal.ModifiedAt.Value));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(journal, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<JournalEntryDto>> Handle(CancelJournalCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var journal = await AccessibleAsync(command.JournalId, cancellationToken).ConfigureAwait(false);
        if (journal is null)
        {
            return Error.NotFound("Journal entry", command.JournalId);
        }

        if (journal.Source is not (JournalSource.Manual or JournalSource.Opening or JournalSource.YearEnd or JournalSource.Bank))
        {
            return Error.Validation($"This journal comes from {journal.Source} ({journal.SourceNumber}); cancel the source document instead.");
        }

        try
        {
            journals.Add(journal.Reverse());
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        if (journal.Source == JournalSource.Bank && journal.SourceId is { } lineId)
        {
            banks.Remove(await banks.MatchesForLineAsync(lineId, cancellationToken).ConfigureAwait(false)); // the statement line is unmatched again
        }

        journal.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(journal, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<JournalEntryDto>> Handle(GetJournalQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var journal = await AccessibleAsync(query.JournalId, cancellationToken).ConfigureAwait(false);
        return journal is null ? Error.NotFound("Journal entry", query.JournalId) : await ToDtoAsync(journal, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<JournalEntryDto>>> Handle(ListJournalsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var names = (await accounts.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
        return Result.Success<IReadOnlyList<JournalEntryDto>>((await journals.ListAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false))
            .Select(j => ToDto(j, names)).ToList());
    }

    /// <summary>Debit and credit totals per leaf account; the two totals are always equal.</summary>
    public async Task<Result<TrialBalanceDto>> Handle(TrialBalanceQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var byId = (await accounts.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id);
        var rows = (await journals.TotalsAsync(query.CompanyId, query.AsOf, cancellationToken).ConfigureAwait(false))
            .Where(t => byId.ContainsKey(t.AccountId) && t.Debit != t.Credit) // accounts that net to zero (e.g. fully reversed) are left out
            .Select(t =>
            {
                var a = byId[t.AccountId];
                var net = t.Debit - t.Credit;
                return new TrialBalanceRow(a.Code, a.Name, a.RootType.ToString(), net > 0 ? net : 0m, net < 0 ? -net : 0m, a.IsDebitNormal ? net : -net);
            })
            .OrderBy(r => r.Code, StringComparer.Ordinal)
            .ToList();
        return new TrialBalanceDto(query.CompanyId, query.AsOf, rows, rows.Sum(r => r.Debit), rows.Sum(r => r.Credit));
    }

    private async Task<JournalEntry?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var journal = await journals.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return journal is not null && await access.CanAccessAsync(journal.CompanyId, cancellationToken).ConfigureAwait(false) ? journal : null;
    }

    private async Task<JournalEntryDto> ToDtoAsync(JournalEntry journal, CancellationToken cancellationToken) =>
        ToDto(journal, (await accounts.ListAsync(journal.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Id));

    internal static JournalEntryDto ToDto(JournalEntry j, IReadOnlyDictionary<Guid, Account> accounts) => new(
        j.Id, j.Name, j.CompanyId, j.PostingDate, j.Remark, j.Source.ToString(), j.SourceNumber, j.DocStatus.ToString(),
        j.Lines.Select(l => new JournalLineDto(accounts.GetValueOrDefault(l.AccountId)?.Code ?? "?", accounts.GetValueOrDefault(l.AccountId)?.Name ?? "?",
            l.Debit, l.Credit, l.Description)).ToList(),
        j.TotalDebit, j.TotalCredit);

    private static AccountDto ToDto(Account a) => new(a.Id, a.Code, a.Name, a.RootType.ToString(), a.IsGroup, a.ParentId);
}
