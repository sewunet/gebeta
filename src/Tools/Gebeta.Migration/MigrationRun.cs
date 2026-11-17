using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Application.Items;
using Gebeta.Core.Application.Parties;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Companies;
using Gebeta.Core.Domain.Items;
using Gebeta.Core.Domain.Parties;
using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;
using Gebeta.Stock.Application;
using Gebeta.Stock.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Migration;

public sealed record MigrationOptions(
    string Company,
    Guid? TargetCompanyId = null,
    string? Tin = null,
    LegalForm LegalForm = LegalForm.PrivateLimitedCompany,
    bool DryRun = false,
    IReadOnlyCollection<string>? Steps = null,
    DateOnly? Cutover = null,
    string? AccountMapPath = null,
    TimeSpan? SettleWait = null,
    DateOnly? HistoryFrom = null)
{
    /// <summary>How long the opening check waits for the server to post the opening stock journals (default one minute).</summary>
    public TimeSpan SettleTimeout => SettleWait ?? TimeSpan.FromMinutes(1);
}

/// <summary>What one step did: created, adopted (an existing Gebeta record with the same key), already migrated, warnings and failures; and its check.</summary>
public sealed class StepReport(string name)
{
    public string Name { get; } = name;
    public int Created { get; set; }
    public int Adopted { get; set; }
    public int AlreadyMigrated { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Failures { get; } = [];
    public string? Check { get; set; }
    public bool CheckPassed { get; set; } = true;
    public bool Ok => Failures.Count == 0 && CheckPassed;
}

/// <summary>
/// One company from ERPNext into Gebeta, in the order of docs/data-migration.md: masters (company, customers and suppliers,
/// items, warehouses), then with a cut-over date the balances on that day (open invoices, opening stock, the opening trial
/// balance). Each record goes through the same command handlers the API uses. A dry run validates every record with the
/// real domain rules and writes nothing.
/// </summary>
public sealed partial class MigrationRun(IServiceProvider services, IErpNextSource source, IdMap idMap, MigrationOptions options)
{
    public static readonly string[] MasterSteps = ["parties", "items", "warehouses", "employees", "structures", "assignments", "history", "payroll"];
    public static readonly string[] CutoverSteps = ["leave", "invoices", "stock", "opening"];
    public static readonly string[] AllSteps = ["company", .. MasterSteps, .. CutoverSteps];

    private Guid _companyId;

    public async Task<IReadOnlyList<StepReport>> RunAsync(CancellationToken cancellationToken)
    {
        // Without --steps: the masters, and the cut-over balances when a cut-over date is given.
        var steps = options.Steps is { Count: > 0 } chosen ? AllSteps.Where(chosen.Contains).ToList()
            : [.. MasterSteps, .. options.Cutover is null ? [] : CutoverSteps];
        if (!options.DryRun)
        {
            await idMap.EnsureAsync(cancellationToken).ConfigureAwait(false);
        }

        var reports = new List<StepReport>();
        var company = await CompanyAsync(reports, cancellationToken).ConfigureAwait(false); // every step needs the company
        if (company is null)
        {
            return reports;
        }

        Func<CancellationToken, Task<StepReport>>[] runners =
        [
            PartiesAsync, ItemsAsync, WarehousesAsync, EmployeesAsync, SalaryStructuresAsync, AssignmentsAsync, SlipHistoryAsync, PayrollCheckAsync,
            ct => Cutover("leave", OpeningLeaveAsync, ct),
            ct => Cutover("invoices", OpenInvoicesAsync, ct),
            ct => Cutover("stock", OpeningStockAsync, ct),
            ct => Cutover("opening", OpeningBalancesAsync, ct),
        ];
        foreach (var (name, run) in AllSteps.Skip(1).Zip(runners))
        {
            if (steps.Contains(name))
            {
                reports.Add(await run(cancellationToken).ConfigureAwait(false));
            }
        }

        return reports;
    }

    private async Task<StepReport> Cutover(string name, Func<DateOnly, StepReport, CancellationToken, Task> step, CancellationToken cancellationToken)
    {
        var report = new StepReport(name);
        if (options.Cutover is not { } cutover)
        {
            report.Failures.Add("Give --cutover yyyy-MM-dd: the last day in ERPNext; balances are brought in as at the end of that day.");
        }
        else
        {
            await step(cutover, report, cancellationToken).ConfigureAwait(false);
        }

        return report;
    }

    /// <summary>The step's proof: every active ERPNext record has a Gebeta counterpart.</summary>
    private async Task CheckAsync(StepReport report, string doctype, int sourceCount, CancellationToken cancellationToken)
    {
        if (options.DryRun)
        {
            report.Check = $"dry run: {report.Created + report.Adopted} of {sourceCount} would be migrated";
            report.CheckPassed = report.Created + report.Adopted + report.Failures.Count == sourceCount;
            return;
        }

        var mapped = (await idMap.LoadAsync(options.Company, doctype, cancellationToken).ConfigureAwait(false)).Count;
        report.CheckPassed = mapped == sourceCount;
        report.Check = $"{mapped} of {sourceCount} active ERPNext records are in Gebeta";
    }

    /// <summary>
    /// ERPNext name → Gebeta id for a migrated master. In a dry run nothing was written, so an active ERPNext record counts as
    /// found (it would have been created by its step) and gets an empty id.
    /// </summary>
    private async Task<Func<string, Guid?>> ResolverAsync(string doctype, CancellationToken cancellationToken)
    {
        if (!options.DryRun)
        {
            var map = await idMap.LoadAsync(options.Company, doctype, cancellationToken).ConfigureAwait(false);
            return name => map.TryGetValue(name, out var id) ? id : null;
        }

        IEnumerable<string> active = doctype switch
        {
            "Party" => (await PartiesToMigrateAsync(cancellationToken).ConfigureAwait(false)).Keys,
            "Item" => (await source.ItemsAsync(cancellationToken).ConfigureAwait(false)).Where(i => !i.Disabled).Select(i => i.Code),
            "Warehouse" => (await source.WarehousesAsync(options.Company, cancellationToken).ConfigureAwait(false)).Where(w => !w.Disabled).Select(w => w.Name),
            "Employee" => (await source.EmployeesAsync(options.Company, cancellationToken).ConfigureAwait(false)).Where(e => e.Status != "Left").Select(e => e.Name),
            "Salary Structure" => (await source.SalaryStructuresAsync(options.Company, cancellationToken).ConfigureAwait(false)).Select(s => s.Name),
            _ => throw new ArgumentOutOfRangeException(nameof(doctype), doctype, null),
        };
        var names = active.ToHashSet(StringComparer.Ordinal);
        return name => names.Contains(name) ? Guid.Empty : null;
    }

    private Task<IReadOnlyDictionary<string, Guid>> KnownAsync(string doctype, CancellationToken cancellationToken) =>
        options.DryRun ? Task.FromResult<IReadOnlyDictionary<string, Guid>>(new Dictionary<string, Guid>()) : idMap.LoadAsync(options.Company, doctype, cancellationToken);

    private async Task<Result<TResult>> Handle<TCommand, TResult>(TCommand command, CancellationToken cancellationToken)
        where TCommand : ICommand<TResult>
    {
        await using var scope = services.CreateAsyncScope(); // one unit of work per record, as one API request would be
        return await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResult>>().Handle(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<TResult>> Query<TQuery, TResult>(TQuery query, CancellationToken cancellationToken)
        where TQuery : IQuery<TResult>
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<TQuery, TResult>>().Handle(query, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>"Habesha Hotel (CUST-0002)": the name people know, then the ERPNext ID to find it by.</summary>
    private static string PartyLabel(string id, string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) || displayName == id ? id : $"{displayName} ({id})";
}
