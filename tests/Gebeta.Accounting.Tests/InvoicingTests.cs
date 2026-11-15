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
    [InlineData(10_001, false, 200.02)] // goods above ETB 10,000: 2%
    [InlineData(3_000, true, 0)]        // services at the threshold: none
    [InlineData(5_000, true, 100)]      // services above ETB 3,000: 2%
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
        Assert.Equal(400m, invoice.WithholdingAmount);
        Assert.Equal(22_600m, invoice.PayableAmount);
        Assert.Equal(22_600m, invoice.Outstanding);
        AssertBalanced(invoice);
        var lines = invoice.JournalLines();
        Assert.Contains(lines, l => l.AccountCode == Invoice.ReceivableAccount && l.Debit == 22_600m);
        Assert.Contains(lines, l => l.AccountCode == Invoice.WithholdingReceivableAccount && l.Debit == 400m);
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
        Assert.Contains(lines, l => l.AccountCode == Invoice.PayableAccount && l.Credit == 5_650m);
        Assert.Contains(lines, l => l.AccountCode == Invoice.WithholdingPayableAccount && l.Credit == 100m);
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
}
