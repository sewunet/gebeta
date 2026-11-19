using System.Globalization;
using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.Application;
using Gebeta.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Accounting.Infrastructure.Persistence;

/// <summary>
/// Submitted invoices still owed (outstanding above zero) whose due date passed at least the threshold's number of days ago (docs/requirements/sap-features.md #9).
/// Credit and debit notes are not invoices to collect or pay, so they are left out. The oldest come first, so a cut at the limit keeps the same ones from run to run.
/// </summary>
internal abstract class OverdueInvoicesCondition(AccountingDbContext db, InvoiceKind kind) : IAlertCondition
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }

    public string RequiredPermission => InvoicingPermissions.InvoicesRead;

    public AlertThreshold? Threshold { get; } = new("days", 1, 1, 365);

    public async Task<IReadOnlyList<AlertItem>> EvaluateAsync(AlertContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var latestDue = context.Today.AddDays(-context.Threshold);
        var invoices = await db.Set<Invoice>().AsNoTracking()
            .Where(i => i.CompanyId == context.CompanyId && i.Kind == kind && !i.IsReturn && i.DocStatus == DocStatus.Submitted && i.Outstanding > 0m && i.DueDate <= latestDue)
            .OrderBy(i => i.DueDate).ThenBy(i => i.Name).Take(AlertLimits.MaxItems)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return invoices.Select(i =>
        {
            var days = context.Today.DayNumber - i.DueDate.DayNumber;
            var title = string.Create(CultureInfo.InvariantCulture, $"{i.Name}: {i.PartyName}, {days} days overdue");
            var body = string.Create(CultureInfo.InvariantCulture, $"Due {i.DueDate:yyyy-MM-dd}; {i.Outstanding:N2} still outstanding.");
            return new AlertItem(i.Id.ToString("N"), title, body, "Invoice", i.Id);
        }).ToList();
    }
}

internal sealed class OverdueSalesInvoicesCondition(AccountingDbContext db) : OverdueInvoicesCondition(db, InvoiceKind.Sales)
{
    public override string Id => "accounting.overdue-sales-invoices";
    public override string Name => "Overdue sales invoices";
    public override string Description => "Sales invoices that customers have not paid by the due date plus the threshold's days.";
}

internal sealed class OverduePurchaseInvoicesCondition(AccountingDbContext db) : OverdueInvoicesCondition(db, InvoiceKind.Purchase)
{
    public override string Id => "accounting.overdue-purchase-invoices";
    public override string Name => "Overdue purchase invoices";
    public override string Description => "Supplier invoices we have not paid by the due date plus the threshold's days.";
}
