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
}
