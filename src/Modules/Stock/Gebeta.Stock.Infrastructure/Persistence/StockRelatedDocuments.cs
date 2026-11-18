using Gebeta.Application;
using Gebeta.Stock.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Stock.Infrastructure.Persistence;

/// <summary>What Stock knows around a document: a stock entry's order (by its reference), and the stock entries that name an order number.</summary>
internal sealed class StockRelatedDocuments(StockDbContext db) : IRelatedDocuments
{
    public async Task<IReadOnlyList<RelatedDocument>> ForAsync(Guid companyId, Guid id, string? number, CancellationToken cancellationToken)
    {
        var entries = db.Set<StockEntry>().AsNoTracking().Where(e => e.CompanyId == companyId);
        var found = new List<RelatedDocument>();
        if (await entries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken).ConfigureAwait(false) is { Reference: { } reference })
        {
            found.Add(new RelatedDocument("Order", null, reference, null, null, null));
        }

        if (number is not null)
        {
            found.AddRange((await entries.Where(e => e.Reference == number).ToListAsync(cancellationToken).ConfigureAwait(false))
                .Select(e => new RelatedDocument("StockEntry", e.Id, e.Name, e.PostingDate, e.DocStatus.ToString(), e.TotalValue)));
        }

        return found;
    }

    public Task<RelatedDocument?> FindAsync(Guid companyId, string number, CancellationToken cancellationToken) => Task.FromResult<RelatedDocument?>(null);
}
