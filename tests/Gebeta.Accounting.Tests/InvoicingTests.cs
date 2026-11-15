using Gebeta.Accounting.Domain.Invoicing;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Tests;

public class InvoicingTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly Guid Party = Guid.CreateVersion7();
    private static readonly DateOnly Date = new(2026, 11, 20);
    private static readonly TaxRules Rules = TaxRules.EthiopianDefault;

    private static Invoice Make(InvoiceKind kind, bool vat, bool withholding, params InvoiceLine[] lines)
    {
        var invoice = Invoice.Create(kind, Company, Party, "Abebe Trading", "0100000001", Date, Date.AddDays(30), lines, Rules, vat, withholding);
        invoice.AssignName(kind == InvoiceKind.Sales ? "SINV-1" : "PINV-1");
        return invoice;
    }

    private static void AssertBalanced(Invoice invoice)
    {
        var lines = invoice.JournalLines();
        Assert.Equal(lines.Sum(l => l.Debit), lines.Sum(l => l.Credit));
    }

    [Fact]
    public void Vat_is_15_percent_only_when_charged()
    {
        var withVat = Make(InvoiceKind.Sales, vat: true, withholding: false, new InvoiceLine("Cement", 10m, 1_000m, "4100", false));
        var withoutVat = Make(InvoiceKind.Sales, vat: false, withholding: false, new InvoiceLine("Cement", 10m, 1_000m, "4100", false));

        Assert.Equal(1_500m, withVat.VatAmount);
        Assert.Equal(11_500m, withVat.GrandTotal);
        Assert.Equal(0m, withoutVat.VatAmount);
        Assert.Equal(10_000m, withoutVat.GrandTotal);
    }

    [Fact]
    public void Vat_exempt_lines_carry_no_vat()
    {
        var invoice = Make(InvoiceKind.Sales, true, false,
            new InvoiceLine("Cement", 1m, 1_000m, "4100", false), new InvoiceLine("Teff (exempt)", 1m, 2_000m, "4100", false, VatApplicable: false));
        Assert.Equal(150m, invoice.VatAmount);
    }

    [Theory]
    [InlineData(10_000, false, 0)]      // goods at the threshold: none
    [InlineData(10_001, false, 300.03)] // goods above ETB 10,000: 3%
    [InlineData(3_000, true, 0)]        // services at the threshold: none
    [InlineData(5_000, true, 150)]      // services above ETB 3,000: 3%
    public void Withholding_applies_above_the_thresholds(decimal amount, bool isService, decimal expected)
    {
        var invoice = Make(InvoiceKind.Purchase, false, true, new InvoiceLine("Item", 1m, amount, "6900", isService));
        Assert.Equal(expected, invoice.WithholdingAmount);
    }

    [Fact]
    public void A_sales_invoice_with_vat_and_withholding_posts_a_balanced_journal()
    {
        var invoice = Make(InvoiceKind.Sales, true, true, new InvoiceLine("Cement", 2m, 10_000m, "4100", false));

        Assert.Equal(20_000m, invoice.NetTotal);
        Assert.Equal(3_000m, invoice.VatAmount);
        Assert.Equal(600m, invoice.WithholdingAmount);
        Assert.Equal(22_400m, invoice.PayableAmount);
        Assert.Equal(22_400m, invoice.Outstanding);
        AssertBalanced(invoice);
        var lines = invoice.JournalLines();
        Assert.Contains(lines, l => l.AccountCode == Invoice.ReceivableAccount && l.Debit == 22_400m);
        Assert.Contains(lines, l => l.AccountCode == Invoice.WithholdingReceivableAccount && l.Debit == 600m);
        Assert.Contains(lines, l => l.AccountCode == Invoice.OutputVatAccount && l.Credit == 3_000m);
        Assert.Contains(lines, l => l.AccountCode == "4100" && l.Credit == 20_000m);
    }

    [Fact]
    public void A_purchase_invoice_posts_input_vat_and_withholding_payable()
    {
        var invoice = Make(InvoiceKind.Purchase, true, true, new InvoiceLine("Consulting", 1m, 5_000m, "6900", true));

        AssertBalanced(invoice);
        var lines = invoice.JournalLines();
        Assert.Contains(lines, l => l.AccountCode == "6900" && l.Debit == 5_000m);
        Assert.Contains(lines, l => l.AccountCode == Invoice.InputVatAccount && l.Debit == 750m);
        Assert.Contains(lines, l => l.AccountCode == Invoice.PayableAccount && l.Credit == 5_600m);
        Assert.Contains(lines, l => l.AccountCode == Invoice.WithholdingPayableAccount && l.Credit == 150m);
    }

    [Fact]
    public void Payments_are_limited_to_the_outstanding_amount_and_block_cancelling()
    {
        var invoice = Make(InvoiceKind.Sales, true, false, new InvoiceLine("Cement", 1m, 1_000m, "4100", false));
        Assert.Equal("accounting.invoice_not_submitted", Assert.Throws<DomainException>(() => invoice.ApplyPayment(100m)).Code);

        invoice.Submit();
        invoice.ApplyPayment(500m);
        Assert.Equal(650m, invoice.Outstanding);
        Assert.Equal("accounting.payment_amount", Assert.Throws<DomainException>(() => invoice.ApplyPayment(650.01m)).Code);
        Assert.Equal("accounting.invoice_paid", Assert.Throws<DomainException>(() => invoice.Cancel()).Code);
    }

    [Fact]
    public void An_invoice_needs_valid_lines_and_dates()
    {
        Assert.Throws<DomainException>(() => Invoice.Create(InvoiceKind.Sales, Company, Party, "X", null, Date, Date, [], Rules, true, false));
        Assert.Throws<DomainException>(() => Invoice.Create(InvoiceKind.Sales, Company, Party, "X", null, Date, Date.AddDays(-1),
            [new InvoiceLine("A", 1m, 1m, "4100", false)], Rules, true, false));
    }

    private static Invoice SubmittedSale()
    {
        var invoice = Make(InvoiceKind.Sales, true, true, new InvoiceLine("Cement", 2m, 10_000m, "4100", false));
        invoice.Submit();
        return invoice;
    }

    [Fact]
    public void A_credit_note_reverses_the_invoice_lines_and_reduces_what_is_owed()
    {
        var sale = SubmittedSale();
        var note = Invoice.CreateReturn(sale, Date.AddDays(3), [new InvoiceLine("Cement", 1m, 10_000m, "4100", false)], Rules);
        note.AssignName("CN-1");

        Assert.Equal("Credit Note", note.DocumentType);
        Assert.Equal(1_500m, note.VatAmount);
        Assert.Equal(300m, note.WithholdingAmount);  // same 3% as the original, no threshold
        Assert.Equal(11_200m, note.PayableAmount);
        AssertBalanced(note);
        Assert.Contains(note.JournalLines(), l => l.AccountCode == Invoice.ReceivableAccount && l.Credit == 11_200m);
        Assert.Contains(note.JournalLines(), l => l.AccountCode == Invoice.OutputVatAccount && l.Debit == 1_500m);

        note.Submit();
        sale.ApplyReturn(note);
        Assert.Equal(11_200m, sale.Outstanding);
        Assert.Equal("accounting.invoice_paid", Assert.Throws<DomainException>(() => sale.Cancel()).Code);
        note.Cancel(); // a submitted note can be cancelled; the original gets the amount back
        sale.ReverseReturn(note);
        Assert.Equal(22_400m, sale.Outstanding);
    }

    [Fact]
    public void A_return_cannot_exceed_what_is_still_owed_or_be_made_against_a_draft()
    {
        var sale = SubmittedSale();
        sale.ApplyPayment(20_000m);
        Assert.Equal("accounting.return_exceeds",
            Assert.Throws<DomainException>(() => Invoice.CreateReturn(sale, Date, [new InvoiceLine("Cement", 1m, 10_000m, "4100", false)], Rules)).Code);

        var draft = Make(InvoiceKind.Sales, true, false, new InvoiceLine("Cement", 1m, 1_000m, "4100", false));
        Assert.Equal("accounting.return_source", Assert.Throws<DomainException>(() => Invoice.CreateReturn(draft, Date, draft.Lines, Rules)).Code);
    }

    [Fact]
    public void An_opening_invoice_carries_only_its_open_amount_and_posts_nothing()
    {
        var opening = Invoice.CreateOpening(InvoiceKind.Purchase, Company, Party, "Old Supplier", null, Date, Date.AddDays(-60), 7_000m, "OLD-778");
        Assert.Equal(Date.AddDays(-60), opening.DueDate);
        Assert.True(opening.IsOpening);
        Assert.Equal(7_000m, opening.Outstanding);
        Assert.Equal(0m, opening.VatAmount);
        Assert.Empty(opening.JournalLines());
        Assert.Throws<DomainException>(() => Invoice.CreateOpening(InvoiceKind.Sales, Company, Party, "X", null, Date, Date, 0m, null));
    }

    [Fact]
    public void A_cancelled_payment_is_owed_again_but_never_more_than_the_invoice()
    {
        var sale = SubmittedSale();
        sale.ApplyPayment(5_000m);
        sale.ReversePayment(5_000m);
        Assert.Equal(22_400m, sale.Outstanding);
        Assert.Equal("accounting.payment_reversal", Assert.Throws<DomainException>(() => sale.ReversePayment(1m)).Code);
    }

    [Fact]
    public void The_withholding_base_is_only_the_part_above_its_threshold()
    {
        // Goods 8,000 (under ETB 10,000) and services 5,000 (over ETB 3,000): only the services are taxed.
        var invoice = Make(InvoiceKind.Purchase, false, true,
            new InvoiceLine("Paper", 1m, 8_000m, "6900", false), new InvoiceLine("Printing", 1m, 5_000m, "6900", true));
        Assert.Equal(5_000m, invoice.WithholdingBase);
        Assert.Equal(3m, invoice.WithholdingRatePercent);
        Assert.Equal(150m, invoice.WithholdingAmount);
        Assert.Equal("Goods and services", invoice.SupplyType);
    }

    [Fact]
    public void Only_a_submitted_purchase_with_withholding_gets_one_receipt()
    {
        var purchase = Make(InvoiceKind.Purchase, false, true, new InvoiceLine("Consulting", 1m, 5_000m, "6900", true));
        Assert.True(purchase.NeedsWithholdingReceipt);
        Assert.Throws<DomainException>(() => purchase.AssignWithholdingReceipt("WHT-1")); // still a draft
        purchase.Submit();
        purchase.AssignWithholdingReceipt("WHT-1");
        Assert.Equal("WHT-1", purchase.WithholdingReceiptNumber);
        Assert.Throws<DomainException>(() => purchase.AssignWithholdingReceipt("WHT-2"));

        var sale = SubmittedSale(); // the customer withholds from us; the receipt is theirs to give
        Assert.False(sale.NeedsWithholdingReceipt);
        Assert.Throws<DomainException>(() => sale.AssignWithholdingReceipt("WHT-3"));
    }

    [Fact]
    public void One_payment_settles_several_invoices_of_the_same_party_and_cancelling_restores_each()
    {
        var first = Make(InvoiceKind.Sales, false, false, new InvoiceLine("Cement", 1m, 10_000m, "4100", false));
        var second = Make(InvoiceKind.Sales, false, false, new InvoiceLine("Sand", 1m, 5_000m, "4100", false));
        first.Submit();
        second.Submit();

        var payment = Payment.Create([(first, 10_000m), (second, 2_000m)], Date, "1120", "CHQ 0042");
        payment.AssignName("PAY-1");
        Assert.Equal(12_000m, payment.Amount);
        Assert.Equal(2, payment.Allocations.Count);
        Assert.Equal(0m, first.Outstanding);
        Assert.Equal(3_000m, second.Outstanding);
        Assert.Contains(payment.JournalLines(), l => l.AccountCode == "1120" && l.Debit == 12_000m);

        payment.Submit();
        payment.CancelAgainst([first, second]);
        Assert.Equal(10_000m, first.Outstanding);
        Assert.Equal(5_000m, second.Outstanding);
    }

    [Fact]
    public void A_payment_cannot_mix_parties_repeat_an_invoice_or_pay_a_note()
    {
        var mine = Make(InvoiceKind.Sales, false, false, new InvoiceLine("Cement", 1m, 1_000m, "4100", false));
        var theirs = Invoice.Create(InvoiceKind.Sales, Company, Guid.CreateVersion7(), "Someone Else", null, Date, Date, [new InvoiceLine("Sand", 1m, 1_000m, "4100", false)],
            Rules, false, false);
        mine.Submit();
        theirs.Submit();
        Assert.Equal("accounting.payment_mixed", Assert.Throws<DomainException>(() => Payment.Create([(mine, 100m), (theirs, 100m)], Date, "1120", null)).Code);
        Assert.Equal("accounting.payment_duplicate", Assert.Throws<DomainException>(() => Payment.Create([(mine, 100m), (mine, 100m)], Date, "1120", null)).Code);
        Assert.Equal(1_000m, theirs.Outstanding);
    }
}
