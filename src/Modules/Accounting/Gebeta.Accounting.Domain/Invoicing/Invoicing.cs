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

    public override string DocumentType => (Kind, IsReturn) switch
    {
        (InvoiceKind.Sales, false) => "Sales Invoice",
        (InvoiceKind.Purchase, false) => "Purchase Invoice",
        (InvoiceKind.Sales, true) => "Credit Note",
        _ => "Debit Note",
    };

    public InvoiceKind Kind { get; private set; }

    /// <summary>A credit note (sales) or debit note (purchase) against <see cref="ReturnAgainstId"/>: reverses part or all of it.</summary>
    public bool IsReturn { get; private set; }

    public Guid? ReturnAgainstId { get; private set; }
    public string? ReturnAgainstNumber { get; private set; }

    /// <summary>An open invoice brought in at cut-over: its balance is already in the opening balances journal, so it posts nothing.</summary>
    public bool IsOpening { get; private set; }
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
        IReadOnlyList<InvoiceLine> lines, TaxRules rules, bool vatCharged, bool withholdingApplies, string? supplierInvoiceNumber = null) =>
        Build(kind, companyId, partyId, partyName, partyTin, postingDate, dueDate, lines, rules, vatCharged, withholdingApplies ? Withholding.AboveThresholds : Withholding.None,
            supplierInvoiceNumber);

    /// <summary>
    /// A credit note (sales) or debit note (purchase) for some or all of a submitted invoice. VAT follows the original;
    /// withholding is taken at the same rate when the original had any, without the thresholds. It reduces what is
    /// still owed on the original, so it cannot exceed the outstanding amount (refunds of paid invoices are not handled yet).
    /// </summary>
    public static Invoice CreateReturn(Invoice original, DateOnly postingDate, IReadOnlyList<InvoiceLine> lines, TaxRules rules)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.DocStatus != DocStatus.Submitted || original.IsReturn || original.IsOpening)
        {
            throw new DomainException("accounting.return_source", "A return can only be made against a submitted invoice.");
        }

        if (postingDate < original.PostingDate)
        {
            throw new DomainException("accounting.return_date", "A return cannot be dated before its invoice.");
        }

        var note = Build(original.Kind, original.CompanyId, original.PartyId, original.PartyName, original.PartyTin, postingDate, postingDate, lines, rules,
            original.VatAmount > 0m, original.WithholdingAmount > 0m ? Withholding.FlatRate : Withholding.None, original.SupplierInvoiceNumber);
        if (note.PayableAmount > original.Outstanding)
        {
            throw new DomainException("accounting.return_exceeds", $"The return ({note.PayableAmount}) is more than is still owed on {original.Name} ({original.Outstanding}).");
        }

        note.IsReturn = true;
        note.ReturnAgainstId = original.Id;
        note.ReturnAgainstNumber = original.Name;
        note.Outstanding = 0m;
        return note;
    }

    /// <summary>An invoice still open at cut-over, for its outstanding amount only (no VAT or withholding; those were dealt with in the old system).</summary>
    public static Invoice CreateOpening(InvoiceKind kind, Guid companyId, Guid partyId, string partyName, string? partyTin, DateOnly postingDate, DateOnly dueDate,
        decimal amount, string? reference)
    {
        if (amount <= 0m)
        {
            throw new DomainException("accounting.opening_amount", "An opening invoice needs a positive outstanding amount.");
        }

        var line = new InvoiceLine($"Opening balance{(string.IsNullOrWhiteSpace(reference) ? "" : " " + reference.Trim())}", 1m, amount,
            kind == InvoiceKind.Sales ? ReceivableAccount : PayableAccount, false, false);
        var invoice = Build(kind, companyId, partyId, partyName, partyTin, postingDate, postingDate, [line], TaxRules.EthiopianDefault, false, Withholding.None, reference);
        invoice.IsOpening = true;
        invoice.DueDate = dueDate; // may be before the cut-over date: the invoice can already be overdue
        return invoice;
    }

    private enum Withholding
    {
        None,
        AboveThresholds,
        FlatRate,
    }

    private static Invoice Build(InvoiceKind kind, Guid companyId, Guid partyId, string partyName, string? partyTin, DateOnly postingDate, DateOnly dueDate,
        IReadOnlyList<InvoiceLine> lines, TaxRules rules, bool vatCharged, Withholding withholdingMode, string? supplierInvoiceNumber)
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
        if (withholdingMode == Withholding.FlatRate)
        {
            withholding = Rounding.ToCurrency(net * rules.WithholdingRatePercent / 100m);
        }
        else if (withholdingMode == Withholding.AboveThresholds)
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

    /// <summary>Journal lines as account codes, balanced by construction. A return posts the same lines with sides swapped; an opening invoice posts nothing.</summary>
    public IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit, string Description)> JournalLines()
    {
        if (IsOpening)
        {
            return [];
        }

        var lines = InvoiceSideLines();
        return IsReturn ? lines.Select(l => (l.AccountCode, l.Credit, l.Debit, l.Description)).ToList() : lines;
    }

    private List<(string AccountCode, decimal Debit, decimal Credit, string Description)> InvoiceSideLines()
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

    /// <summary>A cancelled payment puts its amount back on the invoice.</summary>
    public void ReversePayment(decimal amount)
    {
        if (amount <= 0m || Outstanding + amount > PayableAmount)
        {
            throw new DomainException("accounting.payment_reversal", "The payment does not belong to what was paid on this invoice.");
        }

        Outstanding += amount;
    }

    /// <summary>A submitted credit or debit note reduces what is owed; cancelling it puts the amount back.</summary>
    public void ApplyReturn(Invoice note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (!note.IsReturn || note.ReturnAgainstId != Id)
        {
            throw new DomainException("accounting.return_source", "This note is not against this invoice.");
        }

        ApplyPayment(note.PayableAmount);
    }

    public void ReverseReturn(Invoice note)
    {
        ArgumentNullException.ThrowIfNull(note);
        ReversePayment(note.PayableAmount);
    }

    protected override void OnCancel()
    {
        if (!IsReturn && Outstanding != PayableAmount)
        {
            throw new DomainException("accounting.invoice_paid", "This invoice has payments or returns; cancel them first.");
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
    Task<Payment?> GetPaymentAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Payment>> PaymentsForInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Invoice>> ReturnsAgainstAsync(Guid invoiceId, CancellationToken cancellationToken);
    void Add(Invoice invoice);
    void Add(Payment payment);
}
