using Gebeta.Application;
using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Naming;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Persistence;

/// <summary>One counter per numbering prefix, stored in each module's own schema.</summary>
public sealed class NamingSeriesCounter
{
    public string Key { get; set; } = string.Empty;
    public long Current { get; set; }
}

public static class NamingSeriesModel
{
    public const string Table = "naming_series_counter";

    /// <summary>Adds the counter table to a module's model (and so to its migrations).</summary>
    public static ModelBuilder AddNamingSeriesCounters(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<NamingSeriesCounter>(b =>
        {
            b.ToTable(Table);
            b.HasKey(c => c.Key);
            b.Property(c => c.Key).HasColumnName("key").HasMaxLength(200);
            b.Property(c => c.Current).HasColumnName("current");
        });
        return modelBuilder;
    }
}

/// <summary>
/// Gives documents their numbers from a naming pattern. Numbers are gap-free and never repeat:
/// the counter row is incremented in the same database transaction that saves the document, so a
/// failed save also rolls the number back, and concurrent saves wait on the row lock.
/// Ethiopian tax invoices and receipts need such unbroken sequences.
/// </summary>
public sealed class DocumentNumbering(ICompanyDirectory companies, INamingSeriesSettings settings)
{
    /// <param name="db">The module's DbContext; a transaction must be open on it.</param>
    /// <param name="schema">The module's schema, where its <c>naming_series_counter</c> table lives.</param>
    /// <param name="defaultPattern">The module's pattern when the company has none configured.</param>
    public async Task<string> NextAsync(DbContext db, string schema, Guid companyId, string documentType, string defaultPattern,
        DateOnly date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Document numbers must be taken inside the transaction that saves the document, or numbers could be lost.");
        }

        var company = await companies.GetAsync(companyId, cancellationToken).ConfigureAwait(false)
            ?? throw new DomainException("numbering.company_not_found", $"Company {companyId} was not found.");
        var text = await settings.GetPatternAsync(companyId, documentType, cancellationToken).ConfigureAwait(false) ?? defaultPattern;
        var pattern = NamingPattern.Parse(text);
        if (pattern.IsFailure)
        {
            throw new DomainException("numbering.invalid_pattern", pattern.Error!.Message);
        }

        var key = $"{companyId:N}|{documentType}|{pattern.Value.PrefixFor(date, company.Abbreviation)}";
        var number = await NextCounterAsync(db, schema, key, cancellationToken).ConfigureAwait(false);
        return pattern.Value.Format(date, company.Abbreviation, number);
    }

    private static async Task<long> NextCounterAsync(DbContext db, string schema, string key, CancellationToken cancellationToken)
    {
        if (!schema.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException("Invalid schema name.", nameof(schema));
        }

        // Atomic upsert: inserts 1 for a new prefix, otherwise increments and locks the row until commit.
#pragma warning disable EF1002 // schema is validated above; the key is a parameter
        var table = $"\"{schema}\".{NamingSeriesModel.Table}";
        var sql = $$"""
            INSERT INTO {{table}} (key, current) VALUES ({0}, 1)
            ON CONFLICT (key) DO UPDATE SET current = {{table}}.current + 1
            RETURNING current AS "Value"
            """;
        var values = await db.Database.SqlQueryRaw<long>(sql, key).ToListAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
        return values.Single();
    }
}
