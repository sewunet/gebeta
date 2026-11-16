using MySqlConnector;

namespace Gebeta.Migration;

public sealed record ErpCompany(string Name, string Abbreviation, string? TaxId, string Currency);

public sealed record ErpParty(string Name, string DisplayName, string? TaxId, bool Disabled);

/// <param name="ValuationMethod">"FIFO", "Moving Average" or "LIFO": the item's own, else the Stock Settings default.</param>
public sealed record ErpItem(string Code, string Name, string Uom, string? Group, bool IsStockItem, bool Disabled, string? ValuationMethod = null);

public sealed record ErpWarehouse(string Name, string WarehouseName, bool Disabled);

/// <summary>An account's GL balance at cut-over (debit minus credit, company currency), and the part posted before the fiscal year began.</summary>
public sealed record ErpAccountBalance(string Account, string RootType, string? AccountType, string? AccountNumber, decimal Balance, decimal BalanceBeforeYear);

/// <summary>
/// What is still open against one invoice (or journal) for one party at cut-over, from ERPNext's Payment Ledger. Positive: owed to us
/// (Receivable) or by us (Payable). Negative: an advance or overpayment not allocated to anything.
/// </summary>
public sealed record ErpOutstanding(string AccountType, string PartyType, string Party, string VoucherType, string VoucherNo, decimal Amount, DateOnly? PostingDate, DateOnly? DueDate);

public sealed record ErpFifoLayer(decimal Qty, decimal Rate);

/// <summary>An item's stock in a warehouse at cut-over: the last stock ledger row on or before that day.</summary>
public sealed record ErpStockBalance(string Item, string Warehouse, decimal Qty, decimal Value, IReadOnlyList<ErpFifoLayer> Queue);

/// <summary>What the migration reads from ERPNext. Implemented over ERPNext's MariaDB (read replica in production) and by fakes in tests.</summary>
public interface IErpNextSource
{
    Task<ErpCompany?> CompanyAsync(string name, CancellationToken cancellationToken);
    Task<IReadOnlyList<ErpParty>> CustomersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ErpParty>> SuppliersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ErpItem>> ItemsAsync(CancellationToken cancellationToken);

    /// <summary>The company's warehouses that hold stock (groups are left out).</summary>
    Task<IReadOnlyList<ErpWarehouse>> WarehousesAsync(string company, CancellationToken cancellationToken);

    /// <summary>Every account with GL entries up to <paramref name="asOf"/>; cancelled entries are left out.</summary>
    Task<IReadOnlyList<ErpAccountBalance>> TrialBalanceAsync(string company, DateOnly asOf, DateOnly yearStart, CancellationToken cancellationToken);

    /// <summary>Open amounts per party and invoice at <paramref name="asOf"/>, zero ones left out.</summary>
    Task<IReadOnlyList<ErpOutstanding>> OutstandingAsync(string company, DateOnly asOf, CancellationToken cancellationToken);

    /// <summary>Stock per item and warehouse at <paramref name="asOf"/>, zero quantities left out.</summary>
    Task<IReadOnlyList<ErpStockBalance>> StockBalancesAsync(string company, DateOnly asOf, CancellationToken cancellationToken);
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
        QueryAsync("""
            SELECT name, item_name, stock_uom, item_group, is_stock_item, disabled,
                   COALESCE(NULLIF(valuation_method, ''),
                            (SELECT value FROM tabSingles WHERE doctype = 'Stock Settings' AND field = 'valuation_method'), 'FIFO')
            FROM tabItem WHERE has_variants = 0 ORDER BY name
            """, null,
            r => new ErpItem(r.GetString(0), r.GetString(1), Text(r, 2) ?? "Nos", Text(r, 3), r.GetInt32(4) == 1, r.GetInt32(5) == 1, Text(r, 6)), cancellationToken);

    public Task<IReadOnlyList<ErpWarehouse>> WarehousesAsync(string company, CancellationToken cancellationToken) =>
        QueryAsync("SELECT name, warehouse_name, disabled FROM tabWarehouse WHERE company = @p AND is_group = 0 ORDER BY name", company,
            r => new ErpWarehouse(r.GetString(0), r.GetString(1), r.GetInt32(2) == 1), cancellationToken);

    public Task<IReadOnlyList<ErpAccountBalance>> TrialBalanceAsync(string company, DateOnly asOf, DateOnly yearStart, CancellationToken cancellationToken) =>
        QueryAsync("""
            SELECT a.name, a.root_type, a.account_type, a.account_number,
                   SUM(g.debit - g.credit),
                   SUM(CASE WHEN g.posting_date < @start THEN g.debit - g.credit ELSE 0 END)
            FROM `tabGL Entry` g JOIN tabAccount a ON a.name = g.account
            WHERE g.company = @p AND g.is_cancelled = 0 AND g.posting_date <= @asOf
            GROUP BY a.name, a.root_type, a.account_type, a.account_number
            ORDER BY a.name
            """, company,
            r => new ErpAccountBalance(r.GetString(0), r.GetString(1), Text(r, 2), Text(r, 3), r.GetDecimal(4), r.GetDecimal(5)), cancellationToken,
            ("@asOf", asOf.ToDateTime(TimeOnly.MinValue)), ("@start", yearStart.ToDateTime(TimeOnly.MinValue)));

    // ERPNext v14+ keeps one Payment Ledger row per invoice, payment, allocation and return, signed so that a positive sum is
    // still owed. The invoice's own row (voucher_no = against_voucher_no) gives its date and due date.
    public Task<IReadOnlyList<ErpOutstanding>> OutstandingAsync(string company, DateOnly asOf, CancellationToken cancellationToken) =>
        QueryAsync("""
            SELECT account_type, party_type, party, against_voucher_type, against_voucher_no, SUM(amount),
                   MIN(CASE WHEN voucher_no = against_voucher_no THEN posting_date END),
                   MAX(CASE WHEN voucher_no = against_voucher_no THEN due_date END)
            FROM `tabPayment Ledger Entry`
            WHERE company = @p AND delinked = 0 AND posting_date <= @asOf
            GROUP BY account_type, party_type, party, against_voucher_type, against_voucher_no
            HAVING ABS(SUM(amount)) >= 0.005
            ORDER BY party, against_voucher_no
            """, company,
            r => new ErpOutstanding(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetDecimal(5),
                r.IsDBNull(6) ? null : DateOnly.FromDateTime(r.GetDateTime(6)), r.IsDBNull(7) ? null : DateOnly.FromDateTime(r.GetDateTime(7))),
            cancellationToken, ("@asOf", asOf.ToDateTime(TimeOnly.MinValue)));

    public Task<IReadOnlyList<ErpStockBalance>> StockBalancesAsync(string company, DateOnly asOf, CancellationToken cancellationToken) =>
        QueryAsync("""
            SELECT item_code, warehouse, qty_after_transaction, stock_value, stock_queue FROM (
                SELECT item_code, warehouse, qty_after_transaction, stock_value, stock_queue,
                       ROW_NUMBER() OVER (PARTITION BY item_code, warehouse ORDER BY posting_date DESC, posting_time DESC, creation DESC) AS n
                FROM `tabStock Ledger Entry`
                WHERE company = @p AND is_cancelled = 0 AND posting_date <= @asOf) latest
            WHERE n = 1 AND qty_after_transaction <> 0
            ORDER BY warehouse, item_code
            """, company,
            r => new ErpStockBalance(r.GetString(0), r.GetString(1), r.GetDecimal(2), r.GetDecimal(3), FifoQueue(Text(r, 4))),
            cancellationToken, ("@asOf", asOf.ToDateTime(TimeOnly.MinValue)));

    /// <summary>ERPNext's stock_queue: JSON <c>[[qty, rate], ...]</c> for FIFO items; empty or unreadable gives no layers.</summary>
    public static IReadOnlyList<ErpFifoLayer> FifoQueue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray()
                .Where(l => l.ValueKind == System.Text.Json.JsonValueKind.Array && l.GetArrayLength() >= 2)
                .Select(l => new ErpFifoLayer(l[0].GetDecimal(), l[1].GetDecimal()))
                .ToList();
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static string? Text(MySqlDataReader r, int i) => r.IsDBNull(i) || string.IsNullOrWhiteSpace(r.GetString(i)) ? null : r.GetString(i).Trim();

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, string? parameter, Func<MySqlDataReader, T> map, CancellationToken cancellationToken,
        params (string Name, object Value)[] more)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        if (parameter is not null)
        {
            command.Parameters.AddWithValue("@p", parameter);
        }

        foreach (var (name, value) in more)
        {
            command.Parameters.AddWithValue(name, value);
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
