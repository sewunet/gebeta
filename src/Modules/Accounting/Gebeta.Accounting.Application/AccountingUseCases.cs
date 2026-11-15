using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Contracts;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Application;

public interface IAccountingUnitOfWork : IUnitOfWork;

public interface IAccountingNumbers : IDocumentNumbers;

/// <summary>Creates the company's chart of accounts from the Ethiopian template (once).</summary>
public sealed class ChartSetup(IAccountRepository accounts, TimeProvider clock)
{
    public async Task<bool> EnsureAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await accounts.AnyAsync(companyId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var template in EthiopianChartOfAccounts.Accounts)
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
/// The payroll journal of a submitted salary slip (docs/modules/accounting.md): earnings to salary expense,
/// employer contributions to their expense and to pension payable, income tax and pension deductions to
/// their payables, other deductions to other payroll deductions, and net pay to salaries payable.
/// Runs inside the messaging transaction; a repeated event finds the journal already posted.
/// </summary>
public sealed class SalarySlipSubmittedConsumer(
    ChartSetup chart,
    IAccountRepository accounts,
    IJournalRepository journals,
    IAccountingNumbers numbers,
    IAccountingUnitOfWork unitOfWork,
    TimeProvider clock) : IIntegrationEventConsumer<SalarySlipSubmitted>
{
    private static readonly HashSet<string> TaxComponents = ["IT", "BP_TAX"];
    private static readonly HashSet<string> PensionComponents = ["PE", "BP_DED"];

    public async Task HandleAsync(SalarySlipSubmitted slip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slip);
        if (await journals.FindBySourceAsync(slip.SlipId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return;
        }

        if (await chart.EnsureAsync(slip.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var byCode = (await accounts.ListAsync(slip.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(a => a.Code);
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
            .Select(kv => new JournalLine(byCode[kv.Key].Id, Math.Max(kv.Value, 0m), Math.Max(-kv.Value, 0m), $"Payroll {slip.Number}"))
            .ToList();
        var journal = JournalEntry.Create(slip.CompanyId, slip.PeriodEnd, $"Payroll journal for salary slip {slip.Number}", lines,
            JournalSource.Payroll, slip.SlipId, slip.Number);
        journal.AssignName(await numbers.NextAsync(slip.CompanyId, journal.DocumentType, JournalHandler.DefaultPattern, slip.PeriodEnd, cancellationToken).ConfigureAwait(false));
        journal.CreatedAt = clock.GetUtcNow();
        journal.CreatedBy = "payroll";
        journals.Add(journal);
        journals.Add(journal.Post());
        unitOfWork.Publish(new JournalPosted(journal.Id, journal.Name!, journal.CompanyId, journal.Source.ToString(), journal.SourceId, journal.TotalDebit, journal.CreatedAt));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A cancelled salary slip reverses its payroll journal.</summary>
public sealed class SalarySlipCancelledConsumer(IJournalRepository journals, IAccountingUnitOfWork unitOfWork) : IIntegrationEventConsumer<SalarySlipCancelled>
{
    public async Task HandleAsync(SalarySlipCancelled slip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slip);
        var journal = await journals.FindBySourceAsync(slip.SlipId, cancellationToken).ConfigureAwait(false);
        if (journal is null || journal.DocStatus != DocStatus.Submitted)
        {
            return;
        }

        journals.Add(journal.Reverse());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

// ---------------------------------------------------------------- accounts, journals, reports

public sealed record ListAccountsQuery(Guid CompanyId) : IQuery<IReadOnlyList<AccountDto>>;

public sealed record SetUpChartCommand(Guid CompanyId) : ICommand<IReadOnlyList<AccountDto>>;

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
    ChartSetup chart,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IAccountingNumbers numbers,
    IAccountingUnitOfWork unitOfWork,
    TimeProvider clock)
    : IQueryHandler<ListAccountsQuery, IReadOnlyList<AccountDto>>,
      ICommandHandler<SetUpChartCommand, IReadOnlyList<AccountDto>>,
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

        if (journal.Source != JournalSource.Manual)
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

    private static JournalEntryDto ToDto(JournalEntry j, IReadOnlyDictionary<Guid, Account> accounts) => new(
        j.Id, j.Name, j.CompanyId, j.PostingDate, j.Remark, j.Source.ToString(), j.SourceNumber, j.DocStatus.ToString(),
        j.Lines.Select(l => new JournalLineDto(accounts.GetValueOrDefault(l.AccountId)?.Code ?? "?", accounts.GetValueOrDefault(l.AccountId)?.Name ?? "?",
            l.Debit, l.Credit, l.Description)).ToList(),
        j.TotalDebit, j.TotalCredit);

    private static AccountDto ToDto(Account a) => new(a.Id, a.Code, a.Name, a.RootType.ToString(), a.IsGroup, a.ParentId);
}
