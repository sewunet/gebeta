namespace Gebeta.SharedKernel;

/// <summary>Lifecycle state shared by all documents (docs/architecture.md, section 3).</summary>
public enum DocStatus
{
    Draft = 0,
    Submitted = 1,
    Cancelled = 2,
}

/// <summary>
/// A business document: Draft → Submitted → Cancelled, with amendment of cancelled documents.
/// Submitted documents are immutable; subclasses call <see cref="EnsureDraft"/> before every change.
/// </summary>
public abstract class DocumentBase : Entity
{
    /// <summary>For the persistence layer only (EF Core materialisation).</summary>
    protected DocumentBase()
    {
    }

    protected DocumentBase(Guid companyId, DateOnly postingDate)
    {
        if (companyId == Guid.Empty)
        {
            throw new DomainException("document.company_required", "A document must belong to a company.");
        }

        CompanyId = companyId;
        PostingDate = postingDate;
    }

    /// <summary>The human number from the naming series, e.g. PCR-2026-00012. Set once, when first saved.</summary>
    public string? Name { get; private set; }

    public Guid CompanyId { get; }
    public DateOnly PostingDate { get; private set; }
    public DocStatus DocStatus { get; private set; } = DocStatus.Draft;

    /// <summary>The cancelled document this one replaces.</summary>
    public Guid? AmendedFromId { get; private set; }

    /// <summary>Name of the document type, used in messages, naming series and permissions.</summary>
    public abstract string DocumentType { get; }

    public void AssignName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Name is not null)
        {
            throw new DomainException("document.name_assigned", $"{DocumentType} already has the number {Name}.");
        }

        Name = name;
    }

    public void ChangePostingDate(DateOnly postingDate)
    {
        EnsureDraft();
        PostingDate = postingDate;
    }

    public void Submit()
    {
        if (DocStatus != DocStatus.Draft)
        {
            throw new DomainException("document.not_draft", $"Only a draft {DocumentType} can be submitted; this one is {DocStatus}.");
        }

        ValidateForSubmit();
        OnSubmit();
        DocStatus = DocStatus.Submitted;
    }

    public void Cancel()
    {
        if (DocStatus != DocStatus.Submitted)
        {
            throw new DomainException("document.not_submitted", $"Only a submitted {DocumentType} can be cancelled; this one is {DocStatus}.");
        }

        OnCancel();
        DocStatus = DocStatus.Cancelled;
    }

    /// <summary>Marks this new draft as the amendment of a cancelled document.</summary>
    protected void MarkAsAmendmentOf(DocumentBase cancelled)
    {
        ArgumentNullException.ThrowIfNull(cancelled);
        if (cancelled.DocStatus != DocStatus.Cancelled)
        {
            throw new DomainException("document.not_cancelled", $"Only a cancelled {DocumentType} can be amended.");
        }

        if (cancelled.CompanyId != CompanyId)
        {
            throw new DomainException("document.company_mismatch", "An amendment must belong to the same company.");
        }

        AmendedFromId = cancelled.Id;
    }

    /// <summary>Throws unless the document is still a draft. Call before every change.</summary>
    protected void EnsureDraft()
    {
        if (DocStatus != DocStatus.Draft)
        {
            throw new DomainException("document.read_only", $"This {DocumentType} is {DocStatus} and can no longer be changed.");
        }
    }

    /// <summary>Business validation before submit. Throw <see cref="DomainException"/> to refuse.</summary>
    protected virtual void ValidateForSubmit()
    {
    }

    /// <summary>Work done on submit inside the module's transaction (own ledgers, events).</summary>
    protected virtual void OnSubmit()
    {
    }

    /// <summary>Work done on cancel (reversal entries, events).</summary>
    protected virtual void OnCancel()
    {
    }
}
