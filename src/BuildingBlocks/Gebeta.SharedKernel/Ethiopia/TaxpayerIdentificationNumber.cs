namespace Gebeta.SharedKernel.Ethiopia;

/// <summary>
/// Ethiopian Taxpayer Identification Number (TIN) issued by the Ministry of Revenue: 10 digits.
/// Required on tax invoices, withholding receipts, payroll tax declarations and supplier records.
/// Only the format is checked here; whether a TIN is registered is checked against the Ministry
/// of Revenue when an online service is available.
/// </summary>
public readonly record struct TaxpayerIdentificationNumber
{
    public const int Length = 10;

    private TaxpayerIdentificationNumber(string value) => Value = value;

    public string Value { get; }

    public static Result<TaxpayerIdentificationNumber> Create(string? input)
    {
        var digits = new string((input ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (digits.Length != Length || !digits.All(char.IsAsciiDigit))
        {
            return Error.Validation($"A TIN has exactly {Length} digits; '{input}' does not.");
        }

        return new TaxpayerIdentificationNumber(digits);
    }

    public override string ToString() => Value;
}
