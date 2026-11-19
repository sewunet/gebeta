using Gebeta.Application;
using Gebeta.Stock.Contracts;
using Gebeta.Stock.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Stock.Infrastructure.Persistence;

/// <summary>Which permissions guard this module's records, for the files attached to them (see <see cref="IRecordPermissions"/>).</summary>
internal sealed class StockRecordPermissions(StockDbContext db) : IRecordPermissions
{
    public async Task<RecordPermissions?> ForAsync(Guid companyId, Guid recordId, CancellationToken cancellationToken)
    {
        if (await db.Set<StockEntry>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(StockPermissions.EntriesRead, StockPermissions.EntriesCreate);
        }
        return null;
    }
}
