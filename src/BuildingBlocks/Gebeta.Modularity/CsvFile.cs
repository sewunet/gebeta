using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace Gebeta.Modularity;

/// <summary>Declaration and report files as CSV.</summary>
public static class CsvFile
{
    public static bool Requested(string? format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    /// <summary>UTF-8 CSV with a byte-order mark, so Excel shows Amharic names correctly. Money with two decimals, dates as yyyy-MM-dd.</summary>
    public static IResult Create(string fileName, string[] header, IEnumerable<object?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', header.Select(Quote)));
        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', row.Select(v => Quote(v switch
            {
                null => string.Empty,
                decimal d => d.ToString("0.00", CultureInfo.InvariantCulture),
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty,
            }))));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
