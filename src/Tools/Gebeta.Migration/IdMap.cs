using Npgsql;

namespace Gebeta.Migration;

/// <summary>
/// ERPNext name → Gebeta id, per ERPNext company and doctype (<c>migration.id_map</c>). A record found here was migrated
/// before and is skipped, which makes every run repeatable.
/// </summary>
public sealed class IdMap(string connectionString)
{
    public async Task EnsureAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            CREATE SCHEMA IF NOT EXISTS migration;
            CREATE TABLE IF NOT EXISTS migration.id_map (
                source_company text NOT NULL,
                doctype text NOT NULL,
                source_name text NOT NULL,
                target_id uuid NOT NULL,
                migrated_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (source_company, doctype, source_name));
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, Guid>> LoadAsync(string company, string doctype, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT source_name, target_id FROM migration.id_map WHERE source_company = @c AND doctype = @d", connection);
        command.Parameters.AddWithValue("c", company);
        command.Parameters.AddWithValue("d", doctype);
        var map = new Dictionary<string, Guid>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            map[reader.GetString(0)] = reader.GetGuid(1);
        }

        return map;
    }

    public async Task AddAsync(string company, string doctype, string sourceName, Guid targetId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("""
            INSERT INTO migration.id_map (source_company, doctype, source_name, target_id) VALUES (@c, @d, @n, @t)
            ON CONFLICT (source_company, doctype, source_name) DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("c", company);
        command.Parameters.AddWithValue("d", doctype);
        command.Parameters.AddWithValue("n", sourceName);
        command.Parameters.AddWithValue("t", targetId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<NpgsqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }
}
