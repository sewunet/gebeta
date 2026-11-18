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
using Gebeta.Stock.Domain.Ledger;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Migration;

/// <summary>Masters: company, customers and suppliers, items, warehouses.</summary>
public sealed partial class MigrationRun
{
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

    /// <summary>
    /// ERPNext keeps customers and suppliers apart; one name in both becomes one Gebeta party of type Both. Disabled ones are left
    /// behind, unless they still have open balances or orders at the cut-over: those come over so they can be settled.
    /// </summary>
    private async Task<StepReport> PartiesAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("parties");
        var byName = await PartiesToMigrateAsync(cancellationToken).ConfigureAwait(false);
        foreach (var (name, (party, _)) in byName.Where(p => p.Value.Party.Disabled).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            report.Warnings.Add($"{PartyLabel(name, party.DisplayName)}: disabled in ERPNext but has open balances or orders at the cut-over; migrated so they can be settled.");
        }

        var known = await KnownAsync("Party", cancellationToken).ConfigureAwait(false);
        var inGebeta = known.Count == 0 ? new Dictionary<Guid, PartyDto>()
            : (await Query<ListPartiesQuery, IReadOnlyList<PartyDto>>(new ListPartiesQuery(_companyId), cancellationToken).ConfigureAwait(false)).Value.ToDictionary(p => p.Id);
        foreach (var (name, (party, type)) in byName.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (known.TryGetValue(name, out var existingId))
            {
                report.AlreadyMigrated++;
                // A party brought earlier as only a supplier (or customer) may now be needed as the other too: a disabled customer side
                // with an open order, say. It becomes Both; nothing else about it changes.
                if (inGebeta.TryGetValue(existingId, out var current) && current.Type != type.ToString() && current.Type != nameof(PartyType.Both))
                {
                    var both = await Handle<UpdatePartyCommand, PartyDto>(new UpdatePartyCommand(existingId, current.Name, PartyType.Both, current.Tin,
                        current.VatRegistrationNumber, current.Phone), cancellationToken).ConfigureAwait(false);
                    if (both.IsFailure)
                    {
                        report.Failures.Add($"{name}: could not make it customer and supplier: {both.Error!.Message}");
                    }
                    else
                    {
                        report.Warnings.Add($"{PartyLabel(name, party.DisplayName)}: was only a {current.Type.ToLowerInvariant()} in Gebeta; now customer and supplier.");
                    }
                }

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

    /// <summary>The parties the migration brings: active customers and suppliers, plus disabled ones with open balances or orders at the cut-over.</summary>
    private async Task<Dictionary<string, (ErpParty Party, PartyType Type)>> PartiesToMigrateAsync(CancellationToken cancellationToken)
    {
        var withBalance = new HashSet<string>(StringComparer.Ordinal);
        if (options.Cutover is { } cutover)
        {
            withBalance.UnionWith((await source.OutstandingAsync(options.Company, cutover, cancellationToken).ConfigureAwait(false))
                .Where(o => o.PartyType is "Customer" or "Supplier").Select(o => o.Party));
            withBalance.UnionWith((await source.OpenOrderLinesAsync(options.Company, cutover, cancellationToken).ConfigureAwait(false))
                .Where(l => l.Qty > l.Done).Select(l => l.Party));
        }

        bool Keep(ErpParty p) => !p.Disabled || withBalance.Contains(p.Name);
        var customers = (await source.CustomersAsync(cancellationToken).ConfigureAwait(false)).Where(Keep).ToList();
        var suppliers = (await source.SuppliersAsync(cancellationToken).ConfigureAwait(false)).Where(Keep).ToList();
        var byName = new Dictionary<string, (ErpParty Party, PartyType Type)>(StringComparer.Ordinal);
        foreach (var c in customers)
        {
            byName[c.Name] = (c, PartyType.Customer);
        }

        foreach (var s in suppliers)
        {
            byName[s.Name] = byName.TryGetValue(s.Name, out var both) ? (both.Party with { TaxId = both.Party.TaxId ?? s.TaxId }, PartyType.Both) : (s, PartyType.Supplier);
        }

        return byName;
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

            var unique = Codes.Unique(code, used, 40);
            if (unique != code)
            {
                report.Warnings.Add($"{item.Code}: {code} is taken by another item; this one becomes {unique}.");
                code = unique;
            }

            var method = Valuation(item, report);
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
            if (item.IsStockItem && method != ValuationMethod.MovingAverage) // Gebeta's default; must be set before the item's first movement
            {
                var set = await Handle<SetItemValuationCommand, ItemStockSettingDto>(new SetItemValuationCommand(created.Value.Id, method), cancellationToken).ConfigureAwait(false);
                if (set.IsFailure)
                {
                    report.Failures.Add($"{item.Code}: valuation method {method}: {set.Error!.Message}");
                }
            }
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

            var unique = Codes.Unique(code, used, 20);
            if (unique != code)
            {
                report.Warnings.Add($"{w.Name}: {code} is taken by another warehouse; this one becomes {unique}.");
                code = unique;
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

    /// <summary>ERPNext's valuation method in Gebeta terms. Gebeta has no LIFO (IFRS does not allow it): such items use moving average.</summary>
    private static ValuationMethod Valuation(ErpItem item, StepReport report)
    {
        switch (item.ValuationMethod?.Trim().ToUpperInvariant())
        {
            case "FIFO":
                return ValuationMethod.Fifo;
            case "LIFO":
                report.Warnings.Add($"{item.Code}: LIFO valuation is not allowed under IFRS; Gebeta uses moving average for it.");
                return ValuationMethod.MovingAverage;
            default:
                return ValuationMethod.MovingAverage;
        }
    }
}
