using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Buying.Domain.Agreements;
using Gebeta.Buying.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Buying.Infrastructure.Persistence;

/// <summary>Which permissions guard this module's records, for the files attached to them (see <see cref="IRecordPermissions"/>).</summary>
internal sealed class BuyingRecordPermissions(BuyingDbContext db) : IRecordPermissions
{
    public async Task<RecordPermissions?> ForAsync(Guid companyId, Guid recordId, CancellationToken cancellationToken)
    {
        if (await db.Set<PurchaseOrder>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(BuyingPermissions.OrdersRead, BuyingPermissions.OrdersCreate);
        }
        if (await db.Set<BlanketAgreement>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(BuyingPermissions.AgreementsRead, BuyingPermissions.AgreementsCreate);
        }

        return null;
    }
}
