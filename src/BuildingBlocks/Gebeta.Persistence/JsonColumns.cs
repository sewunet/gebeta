using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Gebeta.Persistence;

/// <summary>Stores a list of small records (slip lines, structure components) as one jsonb column.</summary>
public static class JsonColumns
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static PropertyBuilder<List<T>> IsJsonList<T>(this PropertyBuilder<List<T>> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var converter = new ValueConverter<List<T>, string>(
            v => JsonSerializer.Serialize(v, Options),
            s => JsonSerializer.Deserialize<List<T>>(s, Options) ?? new List<T>());
        var comparer = new ValueComparer<List<T>>(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            v => JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, Options), Options) ?? new List<T>());
        return builder.HasConversion(converter, comparer).HasColumnType("jsonb");
    }
}
