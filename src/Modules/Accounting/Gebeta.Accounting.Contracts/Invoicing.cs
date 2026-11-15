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
    decimal WithholdingAmount, decimal PayableAmount, decimal Outstanding);

public sealed record PaymentDto(Guid Id, string? Number, string Kind, Guid PartyId, string PartyName, Guid InvoiceId, string? InvoiceNumber, decimal Amount,
    string BankAccountCode, DateOnly PostingDate, string? Reference);

public sealed record VatReportRow(string Kind, string? Number, DateOnly Date, string PartyName, string? PartyTin, decimal Net, decimal Vat);

/// <summary>VAT for a period: output VAT on sales minus input VAT on purchases (the monthly VAT return figures).</summary>
public sealed record VatReportDto(DateOnly From, DateOnly To, decimal OutputVat, decimal InputVat, decimal NetPayable, IReadOnlyList<VatReportRow> Rows);

public sealed record WithholdingRow(string? Number, DateOnly Date, string PartyName, string? PartyTin, decimal TaxableAmount, decimal Withheld);

/// <summary>Withholding tax deducted from suppliers in a period (to declare and pay) and withheld by customers (to claim).</summary>
public sealed record WithholdingReportDto(DateOnly From, DateOnly To, decimal TotalWithheldFromSuppliers, decimal TotalWithheldByCustomers,
    IReadOnlyList<WithholdingRow> FromSuppliers, IReadOnlyList<WithholdingRow> ByCustomers);
