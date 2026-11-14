using System.Globalization;

namespace Gebeta.SharedKernel;

/// <summary>
/// An exact amount in one currency. Arithmetic is only allowed between amounts of the same currency.
/// Never use double or float for money (ADR 0003).
/// </summary>
public readonly record struct Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = NormalizeCurrency(currency);
    }

    public static Money Zero(string currency) => new(0m, currency);

    public bool IsZero => Amount == 0m;
    public bool IsNegative => Amount < 0m;

    public Money Round(int precision = Rounding.DefaultCurrencyPrecision) =>
        new(Rounding.ToCurrency(Amount, precision), Currency);

    public Money Negate() => new(-Amount, Currency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value) => value.Negate();

    public static Money operator *(Money value, decimal factor) => new(value.Amount * factor, value.Currency);

    public static Money operator *(decimal factor, Money value) => value * factor;

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;
    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;
    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;
    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    /// <summary>Sums amounts that must all share <paramref name="currency"/>.</summary>
    public static Money Sum(IEnumerable<Money> values, string currency)
    {
        ArgumentNullException.ThrowIfNull(values);
        return values.Aggregate(Zero(currency), (total, next) => total + next);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Amount} {Currency}");

    private static int Compare(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return left.Amount.CompareTo(right.Amount);
    }

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }
    }

    private static string NormalizeCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3 || !currency.Trim().All(char.IsAsciiLetter))
        {
            throw new ArgumentException($"'{currency}' is not a 3-letter currency code.", nameof(currency));
        }

        return currency.Trim().ToUpperInvariant();
    }
}

public sealed class CurrencyMismatchException(string left, string right)
    : DomainException("money.currency_mismatch", $"Cannot combine {left} and {right} amounts.");
