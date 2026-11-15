using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Tax;

/// <summary>
/// One bracket of a monthly employment income tax table, in the Ethiopian "rate minus deduction" form:
/// tax = taxable × rate − deduction, for taxable income above <see cref="Over"/> and up to <see cref="UpTo"/>.
/// </summary>
public sealed record TaxSlab(decimal Over, decimal? UpTo, decimal RatePercent, decimal Deduction);

/// <summary>
/// A monthly employment income tax table valid for a period (docs/ethiopia.md: legal values are data with
/// effective dates). Calculations use the table valid on the payroll date, so back pay for earlier months
/// uses the earlier table automatically.
/// </summary>
public sealed class IncomeTaxTable
{
    private IncomeTaxTable(string code, string name, DateOnly validFrom, DateOnly? validTo, string legalSource, IReadOnlyList<TaxSlab> slabs)
    {
        Code = code;
        Name = name;
        ValidFrom = validFrom;
        ValidTo = validTo;
        LegalSource = legalSource;
        Slabs = slabs;
    }

    public string Code { get; }
    public string Name { get; }
    public DateOnly ValidFrom { get; }
    public DateOnly? ValidTo { get; }

    /// <summary>The proclamation or directive, and any note on what still has to be confirmed.</summary>
    public string LegalSource { get; }

    public IReadOnlyList<TaxSlab> Slabs { get; }

    public static Result<IncomeTaxTable> Create(string code, string name, DateOnly validFrom, DateOnly? validTo, string legalSource, IReadOnlyList<TaxSlab> slabs)
    {
        ArgumentNullException.ThrowIfNull(slabs);
        if (slabs.Count == 0)
        {
            return Error.Validation("A tax table needs at least one slab.");
        }

        var ordered = slabs.OrderBy(s => s.Over).ToList();
        if (ordered[0].Over != 0m)
        {
            return Error.Validation("The first slab must start at 0.");
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var slab = ordered[i];
            var isLast = i == ordered.Count - 1;
            if (slab.RatePercent is < 0 or > 100)
            {
                return Error.Validation("Tax rates are between 0 and 100 percent.");
            }

            if (isLast != (slab.UpTo is null))
            {
                return Error.Validation("Only the last slab is open-ended.");
            }

            if (!isLast && ordered[i + 1].Over != slab.UpTo)
            {
                return Error.Validation($"Slabs must follow each other without gaps (after {slab.UpTo}).");
            }
        }

        if (validTo is { } to && to < validFrom)
        {
            return Error.Validation("A tax table cannot end before it starts.");
        }

        return new IncomeTaxTable(code, name, validFrom, validTo, legalSource, ordered);
    }

    public bool IsValidOn(DateOnly date) => date >= ValidFrom && (ValidTo is null || date <= ValidTo);

    /// <summary>Monthly income tax on <paramref name="taxableIncome"/>, rounded to the cent (never negative).</summary>
    public decimal Calculate(decimal taxableIncome)
    {
        if (taxableIncome <= 0m)
        {
            return 0m;
        }

        var slab = Slabs.Last(s => taxableIncome > s.Over || s.Over == 0m);
        var tax = (taxableIncome * slab.RatePercent / 100m) - slab.Deduction;
        return Rounding.ToCurrency(Math.Max(0m, tax));
    }
}

/// <summary>The Ethiopian employment income tax tables Gebeta ships with.</summary>
public static class EthiopianIncomeTax
{
    /// <summary>
    /// Current table: exempt up to ETB 2,000 a month. These are the slabs the existing Gebeta/ERPNext
    /// payroll uses. VERIFY the amending proclamation number and its effective date with the Ministry of
    /// Revenue before relying on <see cref="IncomeTaxTable.ValidFrom"/>.
    /// </summary>
    public static IncomeTaxTable Current { get; } = IncomeTaxTable.Create(
        "ET-EMP-2025",
        "Ethiopia employment income tax (exempt up to 2,000)",
        new DateOnly(2025, 7, 8),
        null,
        "Federal Income Tax (Amendment) Proclamation, 2025. VERIFY proclamation number and effective date.",
        [
            new TaxSlab(0m, 2_000m, 0m, 0m),
            new TaxSlab(2_000m, 4_000m, 15m, 300m),
            new TaxSlab(4_000m, 7_000m, 20m, 500m),
            new TaxSlab(7_000m, 10_000m, 25m, 850m),
            new TaxSlab(10_000m, 14_000m, 30m, 1_350m),
            new TaxSlab(14_000m, null, 35m, 2_050m),
        ]).Value;

    /// <summary>Previous table (exempt up to ETB 600), kept for back pay and corrections of earlier periods.</summary>
    public static IncomeTaxTable Proclamation979 { get; } = IncomeTaxTable.Create(
        "ET-EMP-979",
        "Ethiopia employment income tax (Proclamation 979/2016)",
        new DateOnly(2016, 7, 8),
        new DateOnly(2025, 7, 7),
        "Federal Income Tax Proclamation No. 979/2016, Schedule A. VERIFY the end date against the amendment.",
        [
            new TaxSlab(0m, 600m, 0m, 0m),
            new TaxSlab(600m, 1_650m, 10m, 60m),
            new TaxSlab(1_650m, 3_200m, 15m, 142.50m),
            new TaxSlab(3_200m, 5_250m, 20m, 302.50m),
            new TaxSlab(5_250m, 7_800m, 25m, 565m),
            new TaxSlab(7_800m, 10_900m, 30m, 955m),
            new TaxSlab(10_900m, null, 35m, 1_500m),
        ]).Value;

    public static IReadOnlyList<IncomeTaxTable> All { get; } = [Current, Proclamation979];

    public static IncomeTaxTable ValidOn(DateOnly date) =>
        All.FirstOrDefault(t => t.IsValidOn(date))
        ?? throw new DomainException("payroll.no_tax_table", $"No income tax table is valid on {date:yyyy-MM-dd}.");
}
