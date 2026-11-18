using Gebeta.Application;
using Gebeta.Buying.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Buying.Infrastructure.Persistence;

/// <summary>Buying keeps no links itself; it recognises its order numbers, which receipts and invoices carry as their reference.</summary>
internal sealed class BuyingRelatedDocuments(BuyingDbContext db) : IRelatedDocuments
{
    public Task<IReadOnlyList<RelatedDocument>> ForAsync(Guid companyId, Guid id, string? number, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RelatedDocument>>([]);

    public async Task<RelatedDocument?> FindAsync(Guid companyId, string number, CancellationToken cancellationToken) =>
        await db.Set<PurchaseOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Name == number, cancellationToken).ConfigureAwait(false) is { } o
            ? new RelatedDocument("PurchaseOrder", o.Id, o.Name, o.PostingDate, o.DocStatus.ToString(), o.NetTotal)
            : null;
}
