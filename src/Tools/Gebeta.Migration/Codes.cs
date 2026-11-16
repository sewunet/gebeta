namespace Gebeta.Migration;

/// <summary>ERPNext names into Gebeta codes: upper case, runs of anything other than letters and digits become one dash, the company suffix dropped.</summary>
public static class Codes
{
    public static string Normalise(string name, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dash = name.LastIndexOf(" - ", StringComparison.Ordinal);
        var core = dash > 0 && name.Length - dash <= 13 ? name[..dash] : name; // "Stores - ADSF" → "Stores"
        var chars = new System.Text.StringBuilder();
        foreach (var c in core.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                chars.Append(c);
            }
            else if (chars.Length > 0 && chars[^1] != '-')
            {
                chars.Append('-');
            }
        }

        var code = chars.ToString().Trim('-');
        return code.Length <= maxLength ? code : code[..maxLength].TrimEnd('-');
    }

    /// <summary>
    /// <paramref name="code"/>, or when it is taken (two source names that clean up to the same code, by case or by the length
    /// limit) <c>CODE-2</c>, <c>CODE-3</c>... The ID map remembers the choice, so later runs keep it.
    /// </summary>
    public static string Unique(string code, ISet<string> taken, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(taken);
        var candidate = code;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            var suffix = $"-{n}";
            candidate = code[..Math.Min(code.Length, maxLength - suffix.Length)].TrimEnd('-') + suffix;
        }

        return candidate;
    }

    /// <summary>
    /// A salary component abbreviation Gebeta accepts (1–20 characters: a letter or _, then letters, digits or _). ERPNext allows any
    /// text ("P7%"), but such an abbreviation cannot appear in a formula, so renaming it changes no calculation.
    /// </summary>
    public static string Abbreviation(string abbreviation, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(abbreviation);
        ArgumentNullException.ThrowIfNull(taken);
        var chars = new System.Text.StringBuilder();
        foreach (var c in abbreviation.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                chars.Append(c);
            }
            else if (chars.Length > 0 && chars[^1] != '_')
            {
                chars.Append('_');
            }
        }

        var clean = chars.ToString().TrimEnd('_');
        if (clean.Length == 0 || char.IsAsciiDigit(clean[0]))
        {
            clean = "C_" + clean;
        }

        clean = clean[..Math.Min(clean.Length, 20)].TrimEnd('_');
        var candidate = clean;
        for (var n = 2; !taken.Add(candidate); n++)
        {
            candidate = $"{clean[..Math.Min(clean.Length, 17)]}_{n}";
        }

        return candidate;
    }

    public static bool IsValidAbbreviation(string abbreviation) =>
        abbreviation.Length is > 0 and <= 20 && (char.IsAsciiLetter(abbreviation[0]) || abbreviation[0] == '_')
        && abbreviation.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
