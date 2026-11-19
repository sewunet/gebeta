using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Payroll.Infrastructure.Persistence;

/// <summary>Which permissions guard this module's records, for the files attached to them (see <see cref="IRecordPermissions"/>).</summary>
internal sealed class PayrollRecordPermissions(PayrollDbContext db) : IRecordPermissions
{
    public async Task<RecordPermissions?> ForAsync(Guid companyId, Guid recordId, CancellationToken cancellationToken)
    {
        if (await db.Set<SalarySlip>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(PayrollPermissions.SlipsRead, PayrollPermissions.SlipsCreate);
        }
        if (await db.Set<AdditionalSalary>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(PayrollPermissions.AdditionalRead, PayrollPermissions.AdditionalManage);
        }
        if (await db.Set<EmployeeLoan>().AsNoTracking().AnyAsync(e => e.Id == recordId && e.CompanyId == companyId, cancellationToken).ConfigureAwait(false))
        {
            return new RecordPermissions(PayrollPermissions.LoansRead, PayrollPermissions.LoansManage);
        }
        return null;
    }
}
