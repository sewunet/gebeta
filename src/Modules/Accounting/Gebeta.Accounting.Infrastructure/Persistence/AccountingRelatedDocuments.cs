using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.Accounting.Domain.Ledger;
using Gebeta.Application;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Accounting.Infrastructure.Persistence;

/// <summary>
/// What Accounting knows around a document: an invoice's order (by number), original or returns, and payments; a payment's invoices; a
/// journal's source; the journal any document posted; and the invoices that name an order number.
/// </summary>
internal sealed class AccountingRelatedDocuments(AccountingDbContext db, IInvoiceRepository invoices) : IRelatedDocuments
{
    public async Task<IReadOnlyList<RelatedDocument>> ForAsync(Guid companyId, Guid id, string? number, CancellationToken cancellationToken)
    {
        var found = new List<RelatedDocument>();
        var all = db.Set<Invoice>().AsNoTracking().Where(i => i.CompanyId == companyId);

        if (await all.FirstOrDefaultAsync(i => i.Id == id, cancellationToken).ConfigureAwait(false) is { } invoice)
        {
            if (invoice.OrderReference is { } order)
            {
                found.Add(new RelatedDocument("Order", null, order, null, null, null));
            }

            var linked = await all.Where(i => i.Id == invoice.ReturnAgainstId || i.ReturnAgainstId == invoice.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
            found.AddRange(linked.Select(Of));
            found.AddRange((await invoices.PaymentsForInvoiceAsync(invoice.Id, cancellationToken).ConfigureAwait(false)).Select(Of));
        }
        else if (await db.Set<Payment>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, cancellationToken).ConfigureAwait(false) is { } payment)
        {
            var ids = payment.Allocations.Select(a => a.InvoiceId).ToList();
            found.AddRange((await all.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(Of));
        }
        else if (await db.Journals.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id && j.CompanyId == companyId, cancellationToken).ConfigureAwait(false) is { SourceId: { } sourceId } journal
                 && SourceType(journal.Source) is { } type)
        {
            found.Add(new RelatedDocument(type, sourceId, journal.SourceNumber, null, null, null));
        }

        // The journal this document posted (an invoice, payment, salary slip, stock entry...).
        found.AddRange((await db.Journals.AsNoTracking().Where(j => j.SourceId == id && j.CompanyId == companyId).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(j => new RelatedDocument("JournalEntry", j.Id, j.Name, j.PostingDate, j.DocStatus.ToString(), j.TotalDebit)));

        if (number is not null)
        {
            found.AddRange((await all.Where(i => i.OrderReference == number).ToListAsync(cancellationToken).ConfigureAwait(false)).Select(Of));
        }

        return found;
    }

    public async Task<RelatedDocument?> FindAsync(Guid companyId, string number, CancellationToken cancellationToken) =>
        await db.Set<Invoice>().AsNoTracking().FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Name == number, cancellationToken).ConfigureAwait(false) is { } i
            ? Of(i)
            : null;

    private static RelatedDocument Of(Invoice i) => new("Invoice", i.Id, i.Name, i.PostingDate, i.DocStatus.ToString(), i.GrandTotal);

    private static RelatedDocument Of(Payment p) => new("Payment", p.Id, p.Name, p.PostingDate, p.DocStatus.ToString(), p.Amount);

    /// <summary>The kind of document a module journal came from, when it has a page of its own.</summary>
    private static string? SourceType(JournalSource source) => source switch
    {
        JournalSource.Payroll => "SalarySlip",
        JournalSource.SalesInvoice or JournalSource.PurchaseInvoice => "Invoice",
        JournalSource.Payment => "Payment",
        JournalSource.Stock => "StockEntry",
        _ => null,
    };
}
