using Gebeta.Application;
using Gebeta.Selling.Contracts;
using Gebeta.Selling.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Selling.Infrastructure.Persistence;

/// <summary>Which permissions guard this module's records, for the files attached to them (see <see cref="IRecordPermissions"/>).</summary>
internal sealed class SellingRecordPermissions(SellingDbContext db) : IRecordPermissions
{
    public async Task<RecordPermissions?> ForAsync(Guid companyId, Guid recordId, CancellationToken cancellationToken)
    {
        if (await db.Set<SalesOrder>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(SellingPermissions.OrdersRead, SellingPermissions.OrdersCreate);
        }
        return null;
    }
}
