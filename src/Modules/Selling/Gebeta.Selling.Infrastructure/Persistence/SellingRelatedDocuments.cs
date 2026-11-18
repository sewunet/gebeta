using Gebeta.Application;
using Gebeta.Selling.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Selling.Infrastructure.Persistence;

/// <summary>Selling keeps no links itself; it recognises its order numbers, which deliveries and invoices carry as their reference.</summary>
internal sealed class SellingRelatedDocuments(SellingDbContext db) : IRelatedDocuments
{
    public Task<IReadOnlyList<RelatedDocument>> ForAsync(Guid companyId, Guid id, string? number, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RelatedDocument>>([]);

    public async Task<RelatedDocument?> FindAsync(Guid companyId, string number, CancellationToken cancellationToken) =>
        await db.Set<SalesOrder>().AsNoTracking().FirstOrDefaultAsync(o => o.CompanyId == companyId && o.Name == number, cancellationToken).ConfigureAwait(false) is { } o
            ? new RelatedDocument("SalesOrder", o.Id, o.Name, o.PostingDate, o.DocStatus.ToString(), o.NetTotal)
            : null;
}
