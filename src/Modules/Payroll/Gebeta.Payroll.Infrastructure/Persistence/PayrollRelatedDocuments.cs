using Gebeta.Application;
using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Payroll.Infrastructure.Persistence;

/// <summary>What Payroll knows around a document: a salary slip's additional salaries, loans and back pay; the slips that took a document's amounts.</summary>
internal sealed class PayrollRelatedDocuments(PayrollDbContext db) : IRelatedDocuments
{
    public async Task<IReadOnlyList<RelatedDocument>> ForAsync(Guid companyId, Guid id, string? number, CancellationToken cancellationToken)
    {
        var entries = await db.AdditionalPay.AsNoTracking().Where(e => e.CompanyId == companyId && (e.SalarySlipId == id || e.SourceId == id))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var found = entries.Where(e => e.SalarySlipId == id && TypeOf(e.SourceType) is not null)
            .Select(e => new RelatedDocument(TypeOf(e.SourceType)!, e.SourceId, e.SourceNumber, null, null, null)).ToList();

        var slipIds = entries.Where(e => e.SourceId == id && e.SalarySlipId is not null).Select(e => e.SalarySlipId!.Value).Distinct().ToList();
        if (slipIds.Count > 0)
        {
            found.AddRange((await db.SalarySlips.AsNoTracking().Where(s => slipIds.Contains(s.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
                .Select(s => new RelatedDocument("SalarySlip", s.Id, s.Name, s.PeriodEnd, s.DocStatus.ToString(), s.NetPay)));
        }

        return found;
    }

    public Task<RelatedDocument?> FindAsync(Guid companyId, string number, CancellationToken cancellationToken) => Task.FromResult<RelatedDocument?>(null);

    private static string? TypeOf(string sourceType) => sourceType switch
    {
        AdditionalSalary.Source => "AdditionalSalary",
        EmployeeLoan.Source => "EmployeeLoan",
        SalaryBackPayment.Source => "BackPayment",
        _ => null,
    };
}
