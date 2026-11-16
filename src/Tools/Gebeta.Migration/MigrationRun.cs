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
    IReadOnlyCollection<string>? Steps = null);

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
/// One company from ERPNext into Gebeta, in the order of docs/data-migration.md: company, customers and suppliers, items,
/// warehouses. Each record goes through the same command handlers the API uses. A dry run validates every record with
/// the real domain rules and writes nothing.
/// </summary>
public sealed class MigrationRun(IServiceProvider services, IErpNextSource source, IdMap idMap, MigrationOptions options)
{
    public static readonly string[] AllSteps = ["company", "parties", "items", "warehouses"];

    private Guid _companyId;

    public async Task<IReadOnlyList<StepReport>> RunAsync(CancellationToken cancellationToken)
    {
        var steps = options.Steps is { Count: > 0 } chosen ? AllSteps.Where(chosen.Contains).ToList() : AllSteps.ToList();
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

        if (steps.Contains("parties"))
        {
            reports.Add(await PartiesAsync(cancellationToken).ConfigureAwait(false));
        }

        if (steps.Contains("items"))
        {
            reports.Add(await ItemsAsync(cancellationToken).ConfigureAwait(false));
        }

        if (steps.Contains("warehouses"))
        {
            reports.Add(await WarehousesAsync(cancellationToken).ConfigureAwait(false));
        }

        return reports;
    }

    private async Task<ErpCompany?> CompanyAsync(List<StepReport> reports, CancellationToken cancellationToken)
    {
        var report = new StepReport("company");
        reports.Add(report);
        var erp = await source.CompanyAsync(options.Company, cancellationToken).ConfigureAwait(false);
        if (erp is null)
        {
            report.Failures.Add($"ERPNext has no company named \"{options.Company}\".");
            return null;
        }

        var known = options.DryRun ? new Dictionary<string, Guid>() : await idMap.LoadAsync(options.Company, "Company", cancellationToken).ConfigureAwait(false);
        if (known.TryGetValue(erp.Name, out var id))
        {
            _companyId = id;
            report.AlreadyMigrated++;
            return erp;
        }

        if (options.TargetCompanyId is { } target)
        {
            // Migrating onto a company already set up in Gebeta.
            if (!options.DryRun)
            {
                await idMap.AddAsync(options.Company, "Company", erp.Name, target, cancellationToken).ConfigureAwait(false);
            }

            _companyId = target;
            report.Adopted++;
            return erp;
        }

        var tin = options.Tin ?? erp.TaxId;
        var check = Company.Create(erp.Name, erp.Abbreviation, options.LegalForm, tin ?? string.Empty);
        if (check.IsFailure)
        {
            report.Failures.Add($"Company \"{erp.Name}\": {check.Error!.Message} (give --tin, or --target-company to use an existing Gebeta company).");
            return null;
        }

        if (options.DryRun)
        {
            report.Created++;
            return erp;
        }

        var created = await Handle<CreateCompanyCommand, CompanyDto>(new CreateCompanyCommand(erp.Name, erp.Abbreviation, options.LegalForm, tin!, null, null), cancellationToken)
            .ConfigureAwait(false);
        if (created.IsFailure)
        {
            report.Failures.Add($"Company \"{erp.Name}\": {created.Error!.Message}");
            return null;
        }

        _companyId = created.Value.Id;
        await idMap.AddAsync(options.Company, "Company", erp.Name, _companyId, cancellationToken).ConfigureAwait(false);
        report.Created++;
        return erp;
    }

    /// <summary>ERPNext keeps customers and suppliers apart; one name in both becomes one Gebeta party of type Both. Disabled ones are left behind.</summary>
    private async Task<StepReport> PartiesAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("parties");
        var customers = (await source.CustomersAsync(cancellationToken).ConfigureAwait(false)).Where(p => !p.Disabled).ToList();
        var suppliers = (await source.SuppliersAsync(cancellationToken).ConfigureAwait(false)).Where(p => !p.Disabled).ToList();
        var byName = new Dictionary<string, (ErpParty Party, PartyType Type)>(StringComparer.Ordinal);
        foreach (var c in customers)
        {
            byName[c.Name] = (c, PartyType.Customer);
        }

        foreach (var s in suppliers)
        {
            byName[s.Name] = byName.TryGetValue(s.Name, out var both) ? (both.Party with { TaxId = both.Party.TaxId ?? s.TaxId }, PartyType.Both) : (s, PartyType.Supplier);
        }

        var known = await KnownAsync("Party", cancellationToken).ConfigureAwait(false);
        foreach (var (name, (party, type)) in byName.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (known.ContainsKey(name))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var tin = party.TaxId;
            if (tin is not null && TaxpayerIdentificationNumber.Create(tin).IsFailure)
            {
                report.Warnings.Add($"{PartyLabel(name, party.DisplayName)}: TIN \"{tin}\" is not 10 digits; imported without a TIN.");
                tin = null;
            }

            var check = Party.Create(_companyId == Guid.Empty ? Guid.NewGuid() : _companyId, party.DisplayName, type, tin, null, null);
            if (check.IsFailure)
            {
                report.Failures.Add($"{name}: {check.Error!.Message}");
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<CreatePartyCommand, PartyDto>(new CreatePartyCommand(_companyId, party.DisplayName, type, tin, null, null), cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"{name}: {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Party", name, created.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        await CheckAsync(report, "Party", byName.Count, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Item codes become upper case without spaces (Gebeta's rule); a code that already exists in Gebeta is adopted, not duplicated.
    /// Disabled items and variant templates are left behind.
    /// </summary>
    private async Task<StepReport> ItemsAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("items");
        var items = (await source.ItemsAsync(cancellationToken).ConfigureAwait(false)).Where(i => !i.Disabled).ToList();
        var known = await KnownAsync("Item", cancellationToken).ConfigureAwait(false);
        var existing = options.DryRun || _companyId == Guid.Empty ? new Dictionary<string, Guid>()
            : (await Query<ListItemsQuery, IReadOnlyList<ItemDto>>(new ListItemsQuery(_companyId), cancellationToken).ConfigureAwait(false)).Value.ToDictionary(i => i.Code, i => i.Id);
        var used = new HashSet<string>(existing.Keys, StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (known.ContainsKey(item.Code))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var code = Codes.Normalise(item.Code, 40);
            if (code != item.Code)
            {
                report.Warnings.Add($"{item.Code}: code becomes {code}.");
            }

            if (existing.TryGetValue(code, out var adopt))
            {
                report.Warnings.Add($"{item.Code}: Gebeta already has item {code}; it is used for this item.");
                if (!options.DryRun)
                {
                    await idMap.AddAsync(options.Company, "Item", item.Code, adopt, cancellationToken).ConfigureAwait(false);
                }

                report.Adopted++;
                continue;
            }

            if (!used.Add(code))
            {
                report.Failures.Add($"{item.Code}: code {code} clashes with another ERPNext item after normalising; rename one of them in ERPNext.");
                continue;
            }

            var check = Item.Create(Guid.NewGuid(), code, item.Name, item.Uom, item.Group, item.IsStockItem);
            if (check.IsFailure)
            {
                report.Failures.Add($"{item.Code}: {check.Error!.Message}");
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<CreateItemCommand, ItemDto>(new CreateItemCommand(_companyId, code, item.Name, item.Uom, item.Group, item.IsStockItem), cancellationToken)
                .ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"{item.Code}: {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Item", item.Code, created.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        await CheckAsync(report, "Item", items.Count, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>Warehouses that hold stock (not groups), coded from their name without the company suffix ("Stores - ADSF" → STORES).</summary>
    private async Task<StepReport> WarehousesAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("warehouses");
        var warehouses = (await source.WarehousesAsync(options.Company, cancellationToken).ConfigureAwait(false)).Where(w => !w.Disabled).ToList();
        var known = await KnownAsync("Warehouse", cancellationToken).ConfigureAwait(false);
        var existing = options.DryRun || _companyId == Guid.Empty ? new Dictionary<string, Guid>()
            : (await Query<ListWarehousesQuery, IReadOnlyList<WarehouseDto>>(new ListWarehousesQuery(_companyId), cancellationToken).ConfigureAwait(false)).Value
                .ToDictionary(w => w.Code, w => w.Id);
        var used = new HashSet<string>(existing.Keys, StringComparer.Ordinal);
        foreach (var w in warehouses)
        {
            if (known.ContainsKey(w.Name))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var code = Codes.Normalise(w.WarehouseName, 20);
            if (existing.TryGetValue(code, out var adopt))
            {
                report.Warnings.Add($"{w.Name}: Gebeta already has warehouse {code}; it is used for this warehouse.");
                if (!options.DryRun)
                {
                    await idMap.AddAsync(options.Company, "Warehouse", w.Name, adopt, cancellationToken).ConfigureAwait(false);
                }

                report.Adopted++;
                continue;
            }

            if (!used.Add(code))
            {
                report.Failures.Add($"{w.Name}: code {code} clashes with another warehouse; rename one of them in ERPNext.");
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<CreateWarehouseCommand, WarehouseDto>(new CreateWarehouseCommand(_companyId, code, w.WarehouseName, null), cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"{w.Name}: {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Warehouse", w.Name, created.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        await CheckAsync(report, "Warehouse", warehouses.Count, cancellationToken).ConfigureAwait(false);
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

/// <summary>ERPNext names into Gebeta codes: upper case, runs of anything other than letters and digits become one dash, the company suffix dropped.</summary>
public static class Codes
{
    public static string Normalise(string name, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dash = name.LastIndexOf(" - ", StringComparison.Ordinal);
        var core = dash > 0 && name.Length - dash <= 13 ? name[..dash] : name; // "Stores - ADSF" → "Stores"
        var chars = new System.Text.StringBuilder();
        foreach (var c in core.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Append(c);
            }
            else if (chars.Length > 0 && chars[^1] != '-')
            {
                chars.Append('-');
            }
        }

        var code = chars.ToString().Trim('-');
        return code.Length <= maxLength ? code : code[..maxLength].TrimEnd('-');
    }
}
