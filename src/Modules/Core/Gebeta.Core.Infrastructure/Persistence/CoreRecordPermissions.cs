using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.CostCenters;
using Gebeta.Core.Domain.Employees;
using Gebeta.Core.Domain.Items;
using Gebeta.Core.Domain.Parties;
using Gebeta.Core.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Core.Infrastructure.Persistence;

/// <summary>Which permissions guard Core's own records, for the files attached to them (see <see cref="IRecordPermissions"/>).</summary>
internal sealed class CoreRecordPermissions(CoreDbContext db) : IRecordPermissions
{
    public async Task<RecordPermissions?> ForAsync(Guid companyId, Guid recordId, CancellationToken cancellationToken)
    {
        // A company's own page: the record is the company.
        if (recordId == companyId)
        {
            return new RecordPermissions(CorePermissions.CompaniesRead, CorePermissions.CompaniesManage);
        }

        if (await db.Set<Employee>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(CorePermissions.EmployeesRead, CorePermissions.EmployeesManage);
        }

        if (await db.Set<Party>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(CorePermissions.PartiesRead, CorePermissions.PartiesManage);
        }

        if (await db.Set<Item>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(CorePermissions.ItemsRead, CorePermissions.ItemsManage);
        }

        if (await db.Set<CostCenter>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(CorePermissions.CostCentersRead, CorePermissions.CostCentersManage);
        }

        // A user belongs to no single company.
        if (await db.Set<User>().AsNoTracking().AnyAsync(e => e.Id == recordId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(CorePermissions.UsersRead, CorePermissions.UsersManage);
        }

        return null;
    }
}
