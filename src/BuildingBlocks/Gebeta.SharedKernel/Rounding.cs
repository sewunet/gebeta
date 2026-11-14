namespace Gebeta.SharedKernel;

/// <summary>
/// The only place where money and quantities are rounded (ADR 0003).
/// Halves round away from zero, as ERPNext and Ethiopian practice do: 2.345 → 2.35, −2.345 → −2.35.
/// </summary>
public static class Rounding
{
    /// <summary>Default number of decimals for a currency amount (ETB, USD, EUR: 2).</summary>
    public const int DefaultCurrencyPrecision = 2;

    public static decimal ToPrecision(decimal value, int decimals)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(decimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(decimals, 9);
        return Math.Round(value, decimals, MidpointRounding.AwayFromZero);
    }

    public static decimal ToCurrency(decimal value, int precision = DefaultCurrencyPrecision) =>
        ToPrecision(value, precision);
}
