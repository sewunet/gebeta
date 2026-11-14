using Gebeta.Core.Domain.Companies;
using Gebeta.SharedKernel.Ethiopia;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class CompanyRepository(CoreDbContext db) : ICompanyRepository
{
    public Task<Company?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Companies.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(string abbreviation, string tin, CancellationToken cancellationToken)
    {
        var parsedTin = TaxpayerIdentificationNumber.Create(tin);
        return parsedTin.IsSuccess
            ? db.Companies.AnyAsync(c => c.Abbreviation == abbreviation || c.Tin == parsedTin.Value, cancellationToken)
            : db.Companies.AnyAsync(c => c.Abbreviation == abbreviation, cancellationToken);
    }

    public async Task<IReadOnlyList<Company>> ListAsync(CancellationToken cancellationToken) =>
        await db.Companies.AsNoTracking().OrderBy(c => c.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(Company company) => db.Companies.Add(company);
}
