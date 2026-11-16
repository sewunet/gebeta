using System.Globalization;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.SharedKernel.Naming;

/// <summary>
/// A document number pattern, written as a naming series: parts separated by dots, e.g.
/// <c>PCR-.YYYY.-.#####</c> → PCR-2026-00012, or <c>INV-.ABBR.-.FY.-.####</c> → INV-ESIG-2019-0001.
///
/// Tokens: <c>YYYY</c>, <c>YY</c>, <c>MM</c>, <c>DD</c> (Gregorian date); <c>EYYYY</c>, <c>EMM</c>
/// (Ethiopian date); <c>FY</c> (Ethiopian fiscal year, Hamle–Sene); <c>ABBR</c> (company abbreviation);
/// one run of <c>#</c> for the number (its length is the zero padding). Anything else is literal text.
/// </summary>
public sealed class NamingPattern
{
    private static readonly HashSet<string> Tokens = ["YYYY", "YY", "MM", "DD", "EYYYY", "EMM", "FY", "ABBR"];

    private readonly IReadOnlyList<string> _parts;
    private readonly int _numberIndex;

    private NamingPattern(string text, IReadOnlyList<string> parts, int numberIndex)
    {
        Text = text;
        _parts = parts;
        _numberIndex = numberIndex;
    }

    public string Text { get; }

    /// <summary>Digits used for the number, e.g. 5 for #####.</summary>
    public int NumberWidth => _parts[_numberIndex].Length;

    public static Result<NamingPattern> Parse(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 100)
        {
            return Error.Validation("A naming pattern is required (at most 100 characters).");
        }

        var parts = pattern.Trim().Split('.').Where(p => p.Length > 0).ToList();
        var numberParts = parts.Select((p, i) => (p, i)).Where(x => x.p.All(c => c == '#')).ToList();
        if (numberParts.Count != 1)
        {
            return Error.Validation($"'{pattern}' must contain exactly one number part made of # (for example .#####).");
        }

        if (numberParts[0].p.Length is < 3 or > 10)
        {
            return Error.Validation("The number part must have 3 to 10 # characters.");
        }

        if (parts.Any(p => p.Contains('#', StringComparison.Ordinal) && !p.All(c => c == '#')))
        {
            return Error.Validation("# may only be used in the number part.");
        }

        return new NamingPattern(pattern.Trim(), parts, numberParts[0].i);
    }

    /// <summary>
    /// Everything except the number, for the given date and company. Numbers are counted separately
    /// for each prefix, so a pattern with .YYYY. or .FY. restarts at 1 every year.
    /// </summary>
    public string PrefixFor(DateOnly date, string companyAbbreviation) =>
        string.Concat(_parts.Take(_numberIndex).Select(p => Render(p, date, companyAbbreviation)));

    public string Format(DateOnly date, string companyAbbreviation, long number)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        var digits = number.ToString(CultureInfo.InvariantCulture).PadLeft(NumberWidth, '0');
        var suffix = string.Concat(_parts.Skip(_numberIndex + 1).Select(p => Render(p, date, companyAbbreviation)));
        return PrefixFor(date, companyAbbreviation) + digits + suffix;
    }

    private static string Render(string part, DateOnly date, string abbreviation)
    {
        if (!Tokens.Contains(part))
        {
            return part;
        }

        var ethiopian = EthiopianDate.FromGregorian(date);
        return part switch
        {
            "YYYY" => date.Year.ToString("0000", CultureInfo.InvariantCulture),
            "YY" => (date.Year % 100).ToString("00", CultureInfo.InvariantCulture),
            "MM" => date.Month.ToString("00", CultureInfo.InvariantCulture),
            "DD" => date.Day.ToString("00", CultureInfo.InvariantCulture),
            "EYYYY" => ethiopian.Year.ToString("0000", CultureInfo.InvariantCulture),
            "EMM" => ethiopian.Month.ToString("00", CultureInfo.InvariantCulture),
            "FY" => EthiopianFiscalYear.Containing(date).Year.ToString("0000", CultureInfo.InvariantCulture),
            "ABBR" => abbreviation,
            _ => part,
        };
    }

    public override string ToString() => Text;
}
