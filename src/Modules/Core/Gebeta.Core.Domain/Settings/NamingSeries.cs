using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Naming;

namespace Gebeta.Core.Domain.Settings;

/// <summary>The number pattern a company uses for one document type, e.g. Petty Cash Request → "PCR-.FY.-.#####".</summary>
public sealed class NamingSeries : Entity
{
    private NamingSeries()
    {
        DocumentType = string.Empty;
        Pattern = string.Empty;
    }

    private NamingSeries(Guid companyId, string documentType, string pattern)
    {
        CompanyId = companyId;
        DocumentType = documentType;
        Pattern = pattern;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>The document type name used by the owning module, e.g. "Petty Cash Request".</summary>
    public string DocumentType { get; private set; }

    public string Pattern { get; private set; }

    public static Result<NamingSeries> Create(Guid companyId, string documentType, string pattern)
    {
        if (companyId == Guid.Empty)
        {
            return Error.Validation("A company is required.");
        }

        if (string.IsNullOrWhiteSpace(documentType) || documentType.Trim().Length > 80)
        {
            return Error.Validation("A document type is required (at most 80 characters).");
        }

        var parsed = NamingPattern.Parse(pattern);
        return parsed.IsFailure ? parsed.Error! : new NamingSeries(companyId, documentType.Trim(), parsed.Value.Text);
    }

    public Result ChangePattern(string pattern)
    {
        var parsed = NamingPattern.Parse(pattern);
        if (parsed.IsFailure)
        {
            return Result.Failure(parsed.Error!);
        }

        Pattern = parsed.Value.Text;
        return Result.Success();
    }
}

public interface INamingSeriesRepository
{
    Task<NamingSeries?> FindAsync(Guid companyId, string documentType, CancellationToken cancellationToken);
    Task<IReadOnlyList<NamingSeries>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(NamingSeries series);
}
