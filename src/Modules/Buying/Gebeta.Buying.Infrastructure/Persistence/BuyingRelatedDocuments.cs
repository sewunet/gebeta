using Gebeta.Application;
using Gebeta.Buying.Domain.Agreements;
using Gebeta.Buying.Domain.Orders;
using Gebeta.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Buying.Infrastructure.Persistence;

/// <summary>
/// An order links to the agreement it calls off against, and an agreement to the submitted orders that call off against it. Receipts and invoices
/// name an order by its number, which this module recognises (<see cref="FindAsync"/>).
/// </summary>
internal sealed class BuyingRelatedDocuments(BuyingDbContext db) : IRelatedDocuments
{
    public async Task<IReadOnlyList<RelatedDocument>> ForAsync(Guid companyId, Guid id, string? number, CancellationToken cancellationToken)
    {
        var found = new List<RelatedDocument>();
        var order = await db.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId, cancellationToken).ConfigureAwait(false);
        if (order?.AgreementId is { } agreementId
            && await db.Set<BlanketAgreement>().AsNoTracking().FirstOrDefaultAsync(a => a.Id == agreementId, cancellationToken).ConfigureAwait(false) is { } agreement)
        {
            found.Add(Of(agreement));
        }

        foreach (var o in await db.Set<PurchaseOrder>().AsNoTracking().Where(o => o.AgreementId == id && o.CompanyId == companyId && o.DocStatus == DocStatus.Submitted)
                     .OrderBy(o => o.PostingDate).ToListAsync(cancellationToken).ConfigureAwait(false))
        {
            found.Add(Of(o));
        }

        return found;
    }

    public async Task<RelatedDocument?> FindAsync(Guid companyId, string number, CancellationToken cancellationToken) =>
        await db.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Name == number, cancellationToken).ConfigureAwait(false) is { } o
            ? Of(o)
            : null;

    private static RelatedDocument Of(PurchaseOrder o) => new("PurchaseOrder", o.Id, o.Name, o.PostingDate, o.DocStatus.ToString(), o.NetTotal);

    private static RelatedDocument Of(BlanketAgreement a) => new("BlanketAgreement", a.Id, a.Name, a.PostingDate, a.DocStatus.ToString(), a.AmountLimit);
}
