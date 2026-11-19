using System.Globalization;
using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Buying.Domain.Agreements;
using Gebeta.Buying.Domain.Orders;
using Gebeta.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Buying.Infrastructure.Persistence;

/// <summary>Submitted framework agreements whose period ends within the threshold's days (from today to that day, so one already over is not announced).</summary>
internal sealed class AgreementsExpiringCondition(BuyingDbContext db) : IAlertCondition
{
    public string Id => "buying.agreements-expiring";
    public string Name => "Framework agreements expiring";
    public string Description => "Agreements with suppliers whose period ends within the given number of days.";
    public string RequiredPermission => BuyingPermissions.AgreementsRead;
    public AlertThreshold? Threshold { get; } = new("days", 30, 1, 365);

    public async Task<IReadOnlyList<AlertItem>> EvaluateAsync(AlertContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var last = context.Today.AddDays(context.Threshold);
        var found = await db.BlanketAgreements.AsNoTracking()
            .Where(a => a.CompanyId == context.CompanyId && a.DocStatus == DocStatus.Submitted && a.ValidTo >= context.Today && a.ValidTo <= last)
            .OrderBy(a => a.ValidTo).ThenBy(a => a.Name).Take(AlertLimits.MaxItems).ToListAsync(cancellationToken).ConfigureAwait(false);

        return found.Select(a =>
        {
            var days = a.ValidTo.DayNumber - context.Today.DayNumber;
            return new AlertItem(a.Id.ToString("N"), string.Create(CultureInfo.InvariantCulture, $"{a.Name}: {a.SupplierName}, ends {a.ValidTo:yyyy-MM-dd} ({days} days left)"),
                null, "BlanketAgreement", a.Id);
        }).ToList();
    }
}

/// <summary>
/// Submitted framework agreements in their period of which at least the threshold's percent has been called off: the agreement's amount, and each item with a planned
/// quantity, separately (an agreement with three planned items can be alerted three times, once each). Called off is what submitted orders hold, as the agreement itself counts it.
/// </summary>
internal sealed class AgreementsNearlyExhaustedCondition(BuyingDbContext db, IPurchaseOrderRepository orders) : IAlertCondition
{
    public string Id => "buying.agreements-nearly-exhausted";
    public string Name => "Framework agreements nearly used up";
    public string Description => "Agreements whose amount, or the planned quantity of an item, has been called off by at least the given percent.";
    public string RequiredPermission => BuyingPermissions.AgreementsRead;
    public AlertThreshold? Threshold { get; } = new("percent", 90, 1, 100);

    public async Task<IReadOnlyList<AlertItem>> EvaluateAsync(AlertContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var agreements = await db.BlanketAgreements.AsNoTracking()
            .Where(a => a.CompanyId == context.CompanyId && a.DocStatus == DocStatus.Submitted && a.ValidFrom <= context.Today && a.ValidTo >= context.Today)
            .OrderBy(a => a.Name).ToListAsync(cancellationToken).ConfigureAwait(false);
        var called = await orders.CalledOffAsync(agreements.Select(a => a.Id).ToList(), cancellationToken).ConfigureAwait(false);

        var items = new List<AlertItem>();
        foreach (var agreement in agreements)
        {
            var taken = called.TryGetValue(agreement.Id, out var o) ? BlanketAgreement.Summarise(o) : [];

            if (agreement.AmountLimit is { } limit && limit > 0m)
            {
                var used = limit - (agreement.RemainingAmount(taken) ?? limit);
                if (used * 100m >= limit * context.Threshold)
                {
                    items.Add(new AlertItem($"{agreement.Id:N}:amount",
                        string.Create(CultureInfo.InvariantCulture, $"{agreement.Name}: {agreement.SupplierName}, {used / limit * 100m:0}% of the amount called off"),
                        string.Create(CultureInfo.InvariantCulture, $"{used:N2} of {limit:N2}; {limit - used:N2} left."), "BlanketAgreement", agreement.Id));
                }
            }

            foreach (var line in agreement.Lines.Where(l => l.PlannedQty is > 0m))
            {
                var left = BlanketAgreement.RemainingQty(line, taken) ?? line.PlannedQty!.Value;
                var used = line.PlannedQty!.Value - left;
                if (used * 100m >= line.PlannedQty.Value * context.Threshold)
                {
                    items.Add(new AlertItem($"{agreement.Id:N}:{line.ItemId:N}",
                        string.Create(CultureInfo.InvariantCulture, $"{agreement.Name}: {line.ItemName}, {used / line.PlannedQty.Value * 100m:0}% of the planned quantity called off"),
                        string.Create(CultureInfo.InvariantCulture, $"{used:0.##} of {line.PlannedQty.Value:0.##} {line.Uom}; {left:0.##} left ({agreement.SupplierName})."), "BlanketAgreement", agreement.Id));
                }
            }
        }

        return items.Take(AlertLimits.MaxItems).ToList();
    }
}
