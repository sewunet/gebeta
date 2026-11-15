using Gebeta.Accounting.Domain.Ledger;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Domain.Invoicing;

/// <summary>
/// Ethiopian VAT and withholding rules as data (docs/ethiopia.md). Defaults: VAT 15% (VAT Proclamation
/// 1341/2024); withholding 2% on goods above ETB 10,000 and services above ETB 3,000 per transaction
/// (Proclamation 979/2016, Art. 53). VERIFY the withholding rate and thresholds against later amendments.
/// </summary>
public sealed record TaxRules(decimal VatRatePercent, decimal WithholdingRatePercent, decimal WithholdingGoodsThreshold, decimal WithholdingServicesThreshold)
{
    public static TaxRules EthiopianDefault { get; } = new(15m, 2m, 10_000m, 3_000m);
}

public enum InvoiceKind
{
    Sales,
    Purchase,
}

/// <summary>An invoice line. <see cref="AccountCode"/> is the income (sales) or expense/asset (purchase) account.</summary>
public sealed record InvoiceLine(string Description, decimal Quantity, decimal Rate, string AccountCode, bool IsService, bool VatApplicable = true)
{
    public decimal Amount => Rounding.ToCurrency(Quantity * Rate);
}

/// <summary>
/// A sales or purchase invoice. Submitting posts it to the general ledger as a journal (receivable or payable,
/// income or expense, VAT, withholding); payments reduce <see cref="Outstanding"/>.
/// </summary>
public sealed class Invoice : DocumentBase
{
    public const string ReceivableAccount = "1200";
    public const string InputVatAccount = "1250";
    public const string WithholdingReceivableAccount = "1260";
    public const string PayableAccount = "2100";
    public const string OutputVatAccount = "2300";
    public const string WithholdingPayableAccount = "2400";

    private List<InvoiceLine> _lines = [];

    private Invoice()
    {
        PartyName = string.Empty;
    }

    private Invoice(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        PartyName = string.Empty;
    }

    public override string DocumentType => Kind == InvoiceKind.Sales ? "Sales Invoice" : "Purchase Invoice";

    public InvoiceKind Kind { get; private set; }
    public Guid PartyId { get; private set; }
    public string PartyName { get; private set; }
    public string? PartyTin { get; private set; }

    /// <summary>The supplier's own invoice number (purchases).</summary>
    public string? SupplierInvoiceNumber { get; private set; }

    public DateOnly DueDate { get; private set; }
    public IReadOnlyList<InvoiceLine> Lines => _lines;
    public decimal NetTotal { get; private set; }
    public decimal VatAmount { get; private set; }
    public decimal WithholdingAmount { get; private set; }

    /// <summary>Net plus VAT: the invoice total.</summary>
    public decimal GrandTotal => NetTotal + VatAmount;

    /// <summary>What the buyer actually pays: the total less withholding.</summary>
    public decimal PayableAmount => GrandTotal - WithholdingAmount;

    public decimal Outstanding { get; private set; }

    /// <param name="vatCharged">Sales: our company is VAT-registered. Purchases: the supplier is.</param>
    /// <param name="withholdingApplies">Purchases: we are a withholding agent. Sales: the customer withholds from us.</param>
    public static Invoice Create(InvoiceKind kind, Guid companyId, Guid partyId, string partyName, string? partyTin, DateOnly postingDate, DateOnly dueDate,
        IReadOnlyList<InvoiceLine> lines, TaxRules rules, bool vatCharged, bool withholdingApplies, string? supplierInvoiceNumber = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(rules);
        if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0m || l.Rate < 0m || string.IsNullOrWhiteSpace(l.AccountCode) || string.IsNullOrWhiteSpace(l.Description)))
        {
            throw new DomainException("accounting.invoice_lines", "An invoice needs lines with a description, an account, a positive quantity and a rate.");
        }

        if (dueDate < postingDate)
        {
            throw new DomainException("accounting.invoice_due", "The due date cannot be before the invoice date.");
        }

        var net = lines.Sum(l => l.Amount);
        var vat = vatCharged ? Rounding.ToCurrency(lines.Where(l => l.VatApplicable).Sum(l => l.Amount) * rules.VatRatePercent / 100m) : 0m;

        var withholding = 0m;
        if (withholdingApplies)
        {
            var goods = lines.Where(l => !l.IsService).Sum(l => l.Amount);
            var services = lines.Where(l => l.IsService).Sum(l => l.Amount);
            if (goods > rules.WithholdingGoodsThreshold)
            {
                withholding += goods * rules.WithholdingRatePercent / 100m;
            }

            if (services > rules.WithholdingServicesThreshold)
            {
                withholding += services * rules.WithholdingRatePercent / 100m;
            }

            withholding = Rounding.ToCurrency(withholding);
        }

        var invoice = new Invoice(companyId, postingDate)
        {
            Kind = kind, PartyId = partyId, PartyName = partyName, PartyTin = partyTin, DueDate = dueDate,
            SupplierInvoiceNumber = string.IsNullOrWhiteSpace(supplierInvoiceNumber) ? null : supplierInvoiceNumber.Trim(),
            _lines = lines.ToList(), NetTotal = net, VatAmount = vat, WithholdingAmount = withholding,
        };
        invoice.Outstanding = invoice.PayableAmount;
        return invoice;
    }

    /// <summary>Journal lines as account codes (+ debit, − credit), balanced by construction.</summary>
    public IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit, string Description)> JournalLines()
    {
        var label = $"{DocumentType} {Name} · {PartyName}";
        var result = new List<(string, decimal, decimal, string)>();
        if (Kind == InvoiceKind.Sales)
        {
            result.Add((ReceivableAccount, PayableAmount, 0m, label));
            if (WithholdingAmount > 0m)
            {
                result.Add((WithholdingReceivableAccount, WithholdingAmount, 0m, $"Withholding by {PartyName}"));
            }

            result.AddRange(_lines.GroupBy(l => l.AccountCode).Select(g => (g.Key, 0m, g.Sum(l => l.Amount), label)));
            if (VatAmount > 0m)
            {
                result.Add((OutputVatAccount, 0m, VatAmount, $"Output VAT {Name}"));
            }
        }
        else
        {
            result.AddRange(_lines.GroupBy(l => l.AccountCode).Select(g => (g.Key, g.Sum(l => l.Amount), 0m, label)));
            if (VatAmount > 0m)
            {
                result.Add((InputVatAccount, VatAmount, 0m, $"Input VAT {Name}"));
            }

            result.Add((PayableAccount, 0m, PayableAmount, label));
            if (WithholdingAmount > 0m)
            {
                result.Add((WithholdingPayableAccount, 0m, WithholdingAmount, $"Withholding from {PartyName}"));
            }
        }

        return result;
    }

    public void ApplyPayment(decimal amount)
    {
        if (DocStatus != DocStatus.Submitted)
        {
            throw new DomainException("accounting.invoice_not_submitted", "Only a submitted invoice can be paid.");
        }

        if (amount <= 0m || amount > Outstanding)
        {
            throw new DomainException("accounting.payment_amount", $"The payment must be between 0 and the outstanding {Outstanding}.");
        }

        Outstanding -= amount;
    }

    protected override void OnCancel()
    {
        if (Outstanding != PayableAmount)
        {
            throw new DomainException("accounting.invoice_paid", "This invoice has payments; cancel the payments first.");
        }
    }
}

public enum PaymentKind
{
    Receive,
    Pay,
}

/// <summary>A payment received from a customer or paid to a supplier, against one invoice.</summary>
public sealed class Payment : DocumentBase
{
    private Payment()
    {
        PartyName = string.Empty;
        BankAccountCode = string.Empty;
    }

    private Payment(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        PartyName = string.Empty;
        BankAccountCode = string.Empty;
    }

    public override string DocumentType => "Payment";

    public PaymentKind Kind { get; private set; }
    public Guid PartyId { get; private set; }
    public string PartyName { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public decimal Amount { get; private set; }
    public string BankAccountCode { get; private set; }
    public string? Reference { get; private set; }

    public static Payment Create(Invoice invoice, decimal amount, DateOnly date, string bankAccountCode, string? reference)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        return new Payment(invoice.CompanyId, date)
        {
            Kind = invoice.Kind == InvoiceKind.Sales ? PaymentKind.Receive : PaymentKind.Pay, PartyId = invoice.PartyId, PartyName = invoice.PartyName,
            InvoiceId = invoice.Id, InvoiceNumber = invoice.Name, Amount = Rounding.ToCurrency(amount),
            BankAccountCode = string.IsNullOrWhiteSpace(bankAccountCode) ? EthiopianChartOfAccounts.Bank : bankAccountCode.Trim(),
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
        };
    }

    public IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit, string Description)> JournalLines()
    {
        var label = $"Payment {Name} · {PartyName} · {InvoiceNumber}";
        return Kind == PaymentKind.Receive
            ? [(BankAccountCode, Amount, 0m, label), (Invoice.ReceivableAccount, 0m, Amount, label)]
            : [(Invoice.PayableAccount, Amount, 0m, label), (BankAccountCode, 0m, Amount, label)];
    }
}

public interface IInvoiceRepository
{
    Task<Invoice?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Invoice>> ListAsync(Guid companyId, InvoiceKind? kind, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    Task<IReadOnlyList<Payment>> PaymentsAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
    void Add(Invoice invoice);
    void Add(Payment payment);
}
