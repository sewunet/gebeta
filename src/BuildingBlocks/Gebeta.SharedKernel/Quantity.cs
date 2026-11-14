using System.Globalization;

namespace Gebeta.SharedKernel;

/// <summary>An exact quantity in one unit of measure (Nos, Kg, Litre, ...).</summary>
public readonly record struct Quantity
{
    public decimal Value { get; }
    public string Uom { get; }

    public Quantity(decimal value, string uom)
    {
        if (string.IsNullOrWhiteSpace(uom))
        {
            throw new ArgumentException("A unit of measure is required.", nameof(uom));
        }

        Value = value;
        Uom = uom.Trim();
    }

    public static Quantity Zero(string uom) => new(0m, uom);

    public bool IsZero => Value == 0m;
    public bool IsNegative => Value < 0m;

    public static Quantity operator +(Quantity left, Quantity right)
    {
        EnsureSameUom(left, right);
        return new Quantity(left.Value + right.Value, left.Uom);
    }

    public static Quantity operator -(Quantity left, Quantity right)
    {
        EnsureSameUom(left, right);
        return new Quantity(left.Value - right.Value, left.Uom);
    }

    public static Quantity operator *(Quantity value, decimal factor) => new(value.Value * factor, value.Uom);

    /// <summary>Converts with a conversion factor, e.g. 2 Box × 12 = 24 Nos.</summary>
    public Quantity ConvertTo(string uom, decimal conversionFactor)
    {
        if (conversionFactor <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(conversionFactor), "The conversion factor must be positive.");
        }

        return new Quantity(Value * conversionFactor, uom);
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Value} {Uom}");

    private static void EnsureSameUom(Quantity left, Quantity right)
    {
        if (!string.Equals(left.Uom, right.Uom, StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException("quantity.uom_mismatch", $"Cannot combine {left.Uom} and {right.Uom} quantities.");
        }
    }
}
