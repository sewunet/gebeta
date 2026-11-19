using Gebeta.Application;
using Gebeta.Accounting.Contracts;
using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.Accounting.Domain.Ledger;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Accounting.Infrastructure.Persistence;

/// <summary>Which permissions guard this module's records, for the files attached to them (see <see cref="IRecordPermissions"/>).</summary>
internal sealed class AccountingRecordPermissions(AccountingDbContext db) : IRecordPermissions
{
    public async Task<RecordPermissions?> ForAsync(Guid companyId, Guid recordId, CancellationToken cancellationToken)
    {
        if (await db.Set<Invoice>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(InvoicingPermissions.InvoicesRead, InvoicingPermissions.InvoicesCreate);
        }
        if (await db.Set<JournalEntry>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(AccountingPermissions.JournalsRead, AccountingPermissions.JournalsCreate);
        }
        return null;
    }
}
