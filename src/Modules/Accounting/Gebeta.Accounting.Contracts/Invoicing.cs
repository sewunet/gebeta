namespace Gebeta.Accounting.Contracts;

public static class InvoicingPermissions
{
    public const string InvoicesRead = "accounting.invoices.read";
    public const string InvoicesCreate = "accounting.invoices.create";
    public const string InvoicesSubmit = "accounting.invoices.submit";
    public const string PaymentsCreate = "accounting.payments.create";
}

public sealed record InvoiceLineDto(string Description, decimal Quantity, decimal Rate, decimal Amount, string AccountCode, bool IsService, bool VatApplicable);

public sealed record InvoiceDto(Guid Id, string? Number, string Kind, Guid CompanyId, Guid PartyId, string PartyName, string? PartyTin, string? SupplierInvoiceNumber,
    DateOnly PostingDate, DateOnly DueDate, string Status, IReadOnlyList<InvoiceLineDto> Lines, decimal NetTotal, decimal VatAmount, decimal GrandTotal,
    decimal WithholdingAmount, decimal PayableAmount, decimal Outstanding, bool IsReturn = false, string? ReturnAgainstNumber = null, bool IsOpening = false,
    string? WithholdingReceiptNumber = null);

public sealed record PaymentDto(Guid Id, string? Number, string Kind, Guid PartyId, string PartyName, Guid InvoiceId, string? InvoiceNumber, decimal Amount,
    string BankAccountCode, DateOnly PostingDate, string? Reference, string Status = "Submitted");

public sealed record VatReportRow(string Kind, string? Number, DateOnly Date, string PartyName, string? PartyTin, decimal Net, decimal Vat);

/// <summary>VAT for a period: output VAT on sales minus input VAT on purchases (the monthly VAT return figures).</summary>
public sealed record VatReportDto(DateOnly From, DateOnly To, decimal OutputVat, decimal InputVat, decimal NetPayable, IReadOnlyList<VatReportRow> Rows);

public sealed record WithholdingRow(string? Number, DateOnly Date, string PartyName, string? PartyTin, decimal TaxableAmount, decimal Withheld,
    string? ReceiptNumber = null, string? SupplyType = null, decimal RatePercent = 0m);

/// <summary>Withholding tax deducted from suppliers in a period (to declare and pay) and withheld by customers (to claim).</summary>
public sealed record WithholdingReportDto(DateOnly From, DateOnly To, decimal TotalWithheldFromSuppliers, decimal TotalWithheldByCustomers,
    IReadOnlyList<WithholdingRow> FromSuppliers, IReadOnlyList<WithholdingRow> ByCustomers);

/// <summary>Open amounts of one customer or supplier by how far past the due date they are.</summary>
public sealed record AgeingRow(Guid PartyId, string PartyName, string? PartyTin, decimal NotDue, decimal Days1To30, decimal Days31To60, decimal Days61To90,
    decimal Over90, decimal Total);

/// <summary>Receivables (sales) or payables (purchases) ageing as of a date.</summary>
public sealed record AgeingReportDto(string Kind, DateOnly AsOf, IReadOnlyList<AgeingRow> Rows, AgeingRow Totals);

/// <summary>
/// The withholding receipt the withholding agent (this company) gives the supplier (Proclamation 979/2016, Art. 53;
/// docs/ethiopia.md). The supplier uses it to claim the tax against its own income tax.
/// </summary>
public sealed record WithholdingReceiptDto(Guid InvoiceId, string ReceiptNumber, DateOnly Date, string Status, string AgentName, string AgentTin,
    string SupplierName, string? SupplierTin, string? InvoiceNumber, string? SupplierInvoiceNumber, string SupplyType, decimal TaxableAmount, decimal RatePercent,
    decimal Withheld);
