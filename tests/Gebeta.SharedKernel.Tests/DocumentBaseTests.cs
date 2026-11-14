using Gebeta.SharedKernel;

namespace Gebeta.SharedKernel.Tests;

public class DocumentBaseTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly DateOnly Today = new(2026, 11, 14);

    [Fact]
    public void A_new_document_is_a_draft()
    {
        var doc = new SampleDocument(Company, Today);

        Assert.Equal(DocStatus.Draft, doc.DocStatus);
        Assert.NotEqual(Guid.Empty, doc.Id);
    }

    [Fact]
    public void Submit_then_cancel_follows_the_lifecycle_and_runs_hooks()
    {
        var doc = new SampleDocument(Company, Today);
        doc.SetAmount(100m);

        doc.Submit();
        Assert.Equal(DocStatus.Submitted, doc.DocStatus);
        Assert.True(doc.Submitted);

        doc.Cancel();
        Assert.Equal(DocStatus.Cancelled, doc.DocStatus);
        Assert.True(doc.Cancelled);
    }

    [Fact]
    public void Validation_can_refuse_submit_and_the_document_stays_a_draft()
    {
        var doc = new SampleDocument(Company, Today);

        var ex = Assert.Throws<DomainException>(doc.Submit);

        Assert.Equal("sample.amount_required", ex.Code);
        Assert.Equal(DocStatus.Draft, doc.DocStatus);
    }

    [Fact]
    public void A_submitted_document_cannot_be_changed_or_submitted_again()
    {
        var doc = new SampleDocument(Company, Today);
        doc.SetAmount(100m);
        doc.Submit();

        Assert.Equal("document.read_only", Assert.Throws<DomainException>(() => doc.SetAmount(5m)).Code);
        Assert.Equal("document.read_only", Assert.Throws<DomainException>(() => doc.ChangePostingDate(Today.AddDays(1))).Code);
        Assert.Equal("document.not_draft", Assert.Throws<DomainException>(doc.Submit).Code);
    }

    [Fact]
    public void Only_a_submitted_document_can_be_cancelled()
    {
        var doc = new SampleDocument(Company, Today);

        Assert.Equal("document.not_submitted", Assert.Throws<DomainException>(doc.Cancel).Code);
    }

    [Fact]
    public void A_cancelled_document_can_be_amended_by_a_new_draft()
    {
        var original = new SampleDocument(Company, Today);
        original.SetAmount(100m);
        original.Submit();
        original.Cancel();

        var amendment = SampleDocument.AmendmentOf(original);

        Assert.Equal(original.Id, amendment.AmendedFromId);
        Assert.Equal(DocStatus.Draft, amendment.DocStatus);
        Assert.Equal(100m, amendment.Amount);
    }

    [Fact]
    public void A_document_that_is_not_cancelled_cannot_be_amended()
    {
        var original = new SampleDocument(Company, Today);

        Assert.Equal("document.not_cancelled", Assert.Throws<DomainException>(() => SampleDocument.AmendmentOf(original)).Code);
    }

    [Fact]
    public void The_number_is_assigned_once()
    {
        var doc = new SampleDocument(Company, Today);
        doc.AssignName("SMP-2026-00001");

        Assert.Equal("document.name_assigned", Assert.Throws<DomainException>(() => doc.AssignName("SMP-2026-00002")).Code);
    }

    [Fact]
    public void A_document_needs_a_company()
    {
        Assert.Equal("document.company_required", Assert.Throws<DomainException>(() => new SampleDocument(Guid.Empty, Today)).Code);
    }

    private sealed class SampleDocument(Guid companyId, DateOnly postingDate) : DocumentBase(companyId, postingDate)
    {
        public override string DocumentType => "Sample";
        public decimal Amount { get; private set; }
        public bool Submitted { get; private set; }
        public bool Cancelled { get; private set; }

        public static SampleDocument AmendmentOf(SampleDocument cancelled)
        {
            var amendment = new SampleDocument(cancelled.CompanyId, cancelled.PostingDate) { Amount = cancelled.Amount };
            amendment.MarkAsAmendmentOf(cancelled);
            return amendment;
        }

        public void SetAmount(decimal amount)
        {
            EnsureDraft();
            Amount = amount;
        }

        protected override void ValidateForSubmit()
        {
            if (Amount <= 0m)
            {
                throw new DomainException("sample.amount_required", "Amount must be positive.");
            }
        }

        protected override void OnSubmit() => Submitted = true;

        protected override void OnCancel() => Cancelled = true;
    }
}
