using System.Text;

namespace Gebeta.SharedKernel.Ethiopia;

/// <summary>
/// An amount written out in English and in Amharic, as vouchers and cheques carry it so the figure cannot be altered
/// (docs/ethiopia.md §5). Birr and santim (2 decimals, rounded half away from zero like the documents), up to 999,999,999,999.99.
/// Negative amounts are written as their absolute value: the document's title says what it is (a credit note, a reversal), never
/// the words.
/// <para>
/// Style, to VERIFY with each customer: "one hundred" / "አንድ መቶ" and "one thousand" / "አንድ ሺህ" are written with "one"; English
/// ends with "only", Amharic with "ብቻ" only when asked. The Amharic spellings (ሥ/ስ, ሦ/ሶ, ሠ/ሰ) are in the tables below, in one
/// place, so a customer's style is a one-line change.
/// </para>
/// </summary>
public static class AmountInWords
{
    public const decimal Maximum = 999_999_999_999.99m;

    private static readonly string[] EnglishOnes =
        ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
         "sixteen", "seventeen", "eighteen", "nineteen"];

    private static readonly string[] EnglishTens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    private static readonly string[] AmharicOnes = ["ዜሮ", "አንድ", "ሁለት", "ሦስት", "አራት", "አምስት", "ስድስት", "ሰባት", "ስምንት", "ዘጠኝ"];

    private static readonly string[] AmharicTens = ["", "አሥር", "ሃያ", "ሠላሳ", "አርባ", "ሃምሳ", "ስልሳ", "ሰባ", "ሰማንያ", "ዘጠና"];

    private const string AmharicTeen = "አሥራ"; // 11–19: አሥራ አንድ, አሥራ ሁለት ...

    private static readonly (long Size, string English, string Amharic)[] Scales =
        [(1_000_000_000L, "billion", "ቢሊዮን"), (1_000_000L, "million", "ሚሊዮን"), (1_000L, "thousand", "ሺህ")];

    /// <summary>The currency's whole and fractional unit, in English and Amharic. Other currencies use their code.</summary>
    private static (string Whole, string Fraction, string AmharicWhole, string AmharicFraction) Units(string currency) => currency.ToUpperInvariant() switch
    {
        "ETB" => ("birr", "santim", "ብር", "ሳንቲም"),
        "USD" => ("dollars", "cents", "ዶላር", "ሳንቲም"),
        "EUR" => ("euros", "cents", "ዩሮ", "ሳንቲም"),
        "GBP" => ("pounds", "pence", "ፓውንድ", "ፔኒ"),
        var code => (code, "cents", code, "ሳንቲም"),
    };

    /// <summary>"One hundred twelve thousand birr only"; "Seventy-five santim only".</summary>
    public static string English(decimal amount, string currency = "ETB")
    {
        var (whole, cents) = Split(amount);
        var (wholeUnit, fractionUnit, _, _) = Units(currency);
        var text = new StringBuilder();
        if (whole > 0 || cents == 0)
        {
            text.Append(EnglishNumber(whole)).Append(' ').Append(wholeUnit);
        }

        if (cents > 0)
        {
            text.Append(whole > 0 ? " and " : string.Empty).Append(EnglishNumber(cents)).Append(' ').Append(fractionUnit);
        }

        text.Append(" only");
        return char.ToUpperInvariant(text[0]) + text.ToString(1, text.Length - 1);
    }

    /// <summary>"አንድ መቶ አሥራ ሁለት ሺህ ብር"; "ሁለት ሺህ ... ብር ከሃያ አምስት ሳንቲም". <paramref name="withOnly"/> adds "ብቻ".</summary>
    public static string Amharic(decimal amount, string currency = "ETB", bool withOnly = false)
    {
        var (whole, cents) = Split(amount);
        var (_, _, wholeUnit, fractionUnit) = Units(currency);
        var parts = new List<string>();
        if (whole > 0 || cents == 0)
        {
            parts.Add($"{AmharicNumber(whole)} {wholeUnit}");
        }

        if (cents > 0)
        {
            parts.Add(whole > 0 ? $"ከ{AmharicNumber(cents)} {fractionUnit}" : $"{AmharicNumber(cents)} {fractionUnit}");
        }

        if (withOnly)
        {
            parts.Add("ብቻ");
        }

        return string.Join(' ', parts);
    }

    private static (long Whole, int Cents) Split(decimal amount)
    {
        var rounded = Rounding.ToCurrency(Math.Abs(amount));
        if (rounded > Maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, $"Amounts in words go up to {Maximum:N2}.");
        }

        var whole = (long)decimal.Truncate(rounded);
        return (whole, (int)((rounded - whole) * 100m));
    }

    private static string EnglishNumber(long n)
    {
        if (n == 0)
        {
            return EnglishOnes[0];
        }

        var words = new List<string>();
        foreach (var (size, name, _) in Scales)
        {
            if (n >= size)
            {
                words.Add($"{EnglishBelowThousand((int)(n / size))} {name}");
                n %= size;
            }
        }

        if (n > 0)
        {
            words.Add(EnglishBelowThousand((int)n));
        }

        return string.Join(' ', words);
    }

    private static string EnglishBelowThousand(int n)
    {
        var words = new List<string>();
        if (n >= 100)
        {
            words.Add($"{EnglishOnes[n / 100]} hundred");
            n %= 100;
        }

        if (n >= 20)
        {
            words.Add(n % 10 == 0 ? EnglishTens[n / 10] : $"{EnglishTens[n / 10]}-{EnglishOnes[n % 10]}");
        }
        else if (n > 0)
        {
            words.Add(EnglishOnes[n]);
        }

        return string.Join(' ', words);
    }

    private static string AmharicNumber(long n)
    {
        if (n == 0)
        {
            return AmharicOnes[0];
        }

        var words = new List<string>();
        foreach (var (size, _, name) in Scales)
        {
            if (n >= size)
            {
                words.Add($"{AmharicBelowThousand((int)(n / size))} {name}");
                n %= size;
            }
        }

        if (n > 0)
        {
            words.Add(AmharicBelowThousand((int)n));
        }

        return string.Join(' ', words);
    }

    private static string AmharicBelowThousand(int n)
    {
        var words = new List<string>();
        if (n >= 100)
        {
            words.Add($"{AmharicOnes[n / 100]} መቶ");
            n %= 100;
        }

        if (n is > 10 and < 20)
        {
            words.Add($"{AmharicTeen} {AmharicOnes[n - 10]}");
        }
        else
        {
            if (n >= 10)
            {
                words.Add(AmharicTens[n / 10]);
                n %= 10;
            }

            if (n > 0)
            {
                words.Add(AmharicOnes[n]);
            }
        }

        return string.Join(' ', words);
    }
}
