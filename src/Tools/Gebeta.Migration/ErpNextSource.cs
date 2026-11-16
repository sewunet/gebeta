using MySqlConnector;

namespace Gebeta.Migration;

public sealed record ErpCompany(string Name, string Abbreviation, string? TaxId, string Currency);

public sealed record ErpParty(string Name, string DisplayName, string? TaxId, bool Disabled);

public sealed record ErpItem(string Code, string Name, string Uom, string? Group, bool IsStockItem, bool Disabled);

public sealed record ErpWarehouse(string Name, string WarehouseName, bool Disabled);

/// <summary>What the migration reads from ERPNext. Implemented over ERPNext's MariaDB (read replica in production) and by fakes in tests.</summary>
public interface IErpNextSource
{
    Task<ErpCompany?> CompanyAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<ErpParty>> CustomersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ErpParty>> SuppliersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ErpItem>> ItemsAsync(CancellationToken cancellationToken);

    /// <summary>The company's warehouses that hold stock (groups are left out).</summary>
    Task<IReadOnlyList<ErpWarehouse>> WarehousesAsync(string company, CancellationToken cancellationToken);
}

/// <summary>Reads ERPNext v14–v16 tables directly (read-only queries; point it at a replica or a copy of production).</summary>
public sealed class MariaDbErpNextSource(string connectionString) : IErpNextSource
{
    public async Task<ErpCompany?> CompanyAsync(string name, CancellationToken cancellationToken) =>
        (await QueryAsync("SELECT name, abbr, tax_id, default_currency FROM tabCompany WHERE name = @p", name,
            r => new ErpCompany(r.GetString(0), r.GetString(1), Text(r, 2), r.GetString(3)), cancellationToken).ConfigureAwait(false)).SingleOrDefault();

    public Task<IReadOnlyList<ErpParty>> CustomersAsync(CancellationToken cancellationToken) =>
        QueryAsync("SELECT name, customer_name, tax_id, disabled FROM tabCustomer ORDER BY name", null,
            r => new ErpParty(r.GetString(0), r.GetString(1), Text(r, 2), r.GetInt32(3) == 1), cancellationToken);

    public Task<IReadOnlyList<ErpParty>> SuppliersAsync(CancellationToken cancellationToken) =>
        QueryAsync("SELECT name, supplier_name, tax_id, disabled FROM tabSupplier ORDER BY name", null,
            r => new ErpParty(r.GetString(0), r.GetString(1), Text(r, 2), r.GetInt32(3) == 1), cancellationToken);

    public Task<IReadOnlyList<ErpItem>> ItemsAsync(CancellationToken cancellationToken) =>
        QueryAsync("SELECT name, item_name, stock_uom, item_group, is_stock_item, disabled FROM tabItem WHERE has_variants = 0 ORDER BY name", null,
            r => new ErpItem(r.GetString(0), r.GetString(1), Text(r, 2) ?? "Nos", Text(r, 3), r.GetInt32(4) == 1, r.GetInt32(5) == 1), cancellationToken);

    public Task<IReadOnlyList<ErpWarehouse>> WarehousesAsync(string company, CancellationToken cancellationToken) =>
        QueryAsync("SELECT name, warehouse_name, disabled FROM tabWarehouse WHERE company = @p AND is_group = 0 ORDER BY name", company,
            r => new ErpWarehouse(r.GetString(0), r.GetString(1), r.GetInt32(2) == 1), cancellationToken);

    private static string? Text(MySqlDataReader r, int i) => r.IsDBNull(i) || string.IsNullOrWhiteSpace(r.GetString(i)) ? null : r.GetString(i).Trim();

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, string? parameter, Func<MySqlDataReader, T> map, CancellationToken cancellationToken)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("@p", parameter);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
