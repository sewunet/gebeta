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

/// <summary>An enabled system user with their roles and the companies they are restricted to (none: every company).</summary>
public sealed record ErpUser(string Name, string? Username, string Email, string? FullName, IReadOnlyList<string> Roles, IReadOnlyList<string> Companies);

/// <summary>
/// A line of a purchase or sales order still open (not completed, closed or cancelled). <see cref="Done"/> is what was received or
/// delivered, in the line's own unit like <see cref="Qty"/>; <see cref="ConversionFactor"/> turns that unit into the stock unit.
/// </summary>
public sealed record ErpOrderLine(string Kind, string Order, string Party, DateOnly OrderDate, DateOnly? DueDate, string Item, string? Warehouse,
    decimal Qty, decimal Done, decimal Rate, decimal ConversionFactor, decimal BilledAmount);

/// <summary>
/// An employee's balance of one leave type at cut-over, from ERPNext's leave ledger: every entry started by then (allocations add;
/// applications, encashments and expiries take away). <see cref="AllocationCoversDate"/>: an allocation of this type runs over the day.
/// </summary>
public sealed record ErpLeaveBalance(string Employee, string LeaveType, decimal Balance, bool AllocationCoversDate);

/// <summary>An employee. Ethiopian names as usually kept in ERPNext: first name, father's name (middle_name), grandfather's name (last_name).</summary>
public sealed record ErpEmployee(string Name, string FirstName, string? FatherName, string? GrandfatherName, string FullName, string? Gender, DateOnly DateOfJoining,
    string Status, DateOnly? RelievingDate, string? Tin);

/// <summary>
/// A salary structure or salary slip row (ERPNext "Salary Detail"). <see cref="Formula"/> is set only when the amount is based on it.
/// <see cref="TaxSlabBased"/>: the income tax row, worked out from the tax slabs. <see cref="AdditionalAmount"/>: the part of a slip row
/// that came from an Additional Salary (overtime, arrears).
/// </summary>
public sealed record ErpSalaryDetail(string Parent, bool IsDeduction, string Component, string Abbreviation, string? Formula, decimal Amount, string? Condition,
    bool IsTaxable, bool DependsOnPaymentDays, bool Statistical, bool NotInTotal, bool TaxSlabBased, decimal AdditionalAmount = 0m, bool FromAdditionalSalary = false);

public sealed record ErpSalaryStructure(string Name, IReadOnlyList<ErpSalaryDetail> Rows);

/// <param name="Created">When it was entered: a raise entered after a slip was made did not count for that slip (it is back pay).</param>
/// <param name="Name">ERPNext's id of the assignment, to read its other fields (formulas may use them).</param>
public sealed record ErpAssignment(string Employee, string Structure, decimal Base, DateOnly FromDate, DateTime? Created = null, string? Name = null);

/// <summary>
/// What an ERPNext salary formula can read besides the structure's own components (hrms, salary_slip.get_data_for_eval): every field
/// of the salary structure assignment, the slip and the employee, and every salary component abbreviation in the system (0 when the
/// component is not in the structure).
/// </summary>
public sealed record ErpFormulaScope(IReadOnlySet<string> AssignmentFields, IReadOnlySet<string> EmployeeFields, IReadOnlySet<string> SlipFields,
    IReadOnlySet<string> ComponentAbbreviations)
{
    public static ErpFormulaScope Empty { get; } = new(new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), new HashSet<string>());
}

public sealed record ErpSalarySlip(string Name, string Employee, string Structure, DateOnly StartDate, DateOnly EndDate, decimal PaymentDays, decimal TotalWorkingDays,
    decimal GrossPay, decimal NetPay, IReadOnlyList<ErpSalaryDetail> Rows, DateTime? Created = null);

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

    Task<IReadOnlyList<ErpEmployee>> EmployeesAsync(string company, CancellationToken cancellationToken);

    Task<IReadOnlyList<ErpUser>> UsersAsync(CancellationToken cancellationToken);

    /// <summary>Lines of submitted purchase and sales orders dated by <paramref name="asOf"/> that are not completed, closed or cancelled.</summary>
    Task<IReadOnlyList<ErpOrderLine>> OpenOrderLinesAsync(string company, DateOnly asOf, CancellationToken cancellationToken);

    Task<IReadOnlyList<ErpLeaveBalance>> LeaveBalancesAsync(string company, DateOnly asOf, CancellationToken cancellationToken);

    /// <summary>Submitted salary structures, active or still named by an assignment, with their earnings and deductions in order.</summary>
    Task<IReadOnlyList<ErpSalaryStructure>> SalaryStructuresAsync(string company, CancellationToken cancellationToken);

    /// <summary>Every submitted salary structure assignment (the history, not only the latest).</summary>
    Task<IReadOnlyList<ErpAssignment>> AssignmentsAsync(string company, CancellationToken cancellationToken);

    Task<ErpFormulaScope> FormulaScopeAsync(CancellationToken cancellationToken);

    /// <summary>Fields of ERPNext records by name (doctype "Employee" or "Salary Structure Assignment"), as text; null where empty.</summary>
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>> FieldValuesAsync(string doctype, string company, IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken);

    /// <summary>Every submitted salary slip whose period starts on or after <paramref name="from"/> (any, when null) and ends by <paramref name="to"/>.</summary>
    Task<IReadOnlyList<ErpSalarySlip>> SlipsAsync(string company, DateOnly? from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The submitted salary slips of the latest payroll period ending on or before <paramref name="asOf"/> (any, when null).</summary>
    Task<IReadOnlyList<ErpSalarySlip>> LatestSlipsAsync(string company, DateOnly? asOf, CancellationToken cancellationToken);
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

    public async Task<IReadOnlyList<ErpEmployee>> EmployeesAsync(string company, CancellationToken cancellationToken)
    {
        // tin_number is a custom field of the Ethiopian setup; other sites may not have it.
        var hasTin = (await QueryAsync(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'tabEmployee' AND column_name = 'tin_number'",
            null, r => r.GetInt64(0), cancellationToken).ConfigureAwait(false)).Single() > 0;
        return await QueryAsync($"""
            SELECT name, first_name, middle_name, last_name, employee_name, gender, date_of_joining, status, relieving_date, {(hasTin ? "tin_number" : "NULL")}
            FROM tabEmployee WHERE company = @p ORDER BY name
            """, company,
            r => new ErpEmployee(r.GetString(0), Text(r, 1) ?? r.GetString(4), Text(r, 2), Text(r, 3), r.GetString(4), Text(r, 5), DateOnly.FromDateTime(r.GetDateTime(6)),
                r.GetString(7), r.IsDBNull(8) ? null : DateOnly.FromDateTime(r.GetDateTime(8)), Text(r, 9)), cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ErpUser>> UsersAsync(CancellationToken cancellationToken)
    {
        var roles = (await QueryAsync("SELECT parent, role FROM `tabHas Role` WHERE parenttype = 'User'", null, r => (r.GetString(0), r.GetString(1)), cancellationToken)
            .ConfigureAwait(false)).ToLookup(x => x.Item1, x => x.Item2, StringComparer.Ordinal);
        var companies = (await QueryAsync("SELECT user, for_value FROM `tabUser Permission` WHERE allow = 'Company'", null, r => (r.GetString(0), r.GetString(1)), cancellationToken)
            .ConfigureAwait(false)).ToLookup(x => x.Item1, x => x.Item2, StringComparer.Ordinal);
        return await QueryAsync("""
            SELECT name, username, email, full_name FROM tabUser
            WHERE enabled = 1 AND user_type = 'System User' AND name NOT IN ('Administrator', 'Guest')
            ORDER BY name
            """, null,
            r => new ErpUser(r.GetString(0), Text(r, 1), Text(r, 2) ?? r.GetString(0), Text(r, 3),
                roles[r.GetString(0)].Distinct().Order(StringComparer.Ordinal).ToList(), companies[r.GetString(0)].Distinct().ToList()),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ErpOrderLine>> OpenOrderLinesAsync(string company, DateOnly asOf, CancellationToken cancellationToken) =>
        QueryAsync("""
            SELECT 'Purchase', o.name, o.supplier, o.transaction_date, o.schedule_date, i.item_code, i.warehouse,
                   i.qty, i.received_qty, i.rate, i.conversion_factor, i.billed_amt, i.idx
            FROM `tabPurchase Order` o JOIN `tabPurchase Order Item` i ON i.parent = o.name
            WHERE o.company = @p AND o.docstatus = 1 AND o.status NOT IN ('Completed', 'Closed', 'Cancelled') AND o.transaction_date <= @asOf
            UNION ALL
            SELECT 'Sales', o.name, o.customer, o.transaction_date, o.delivery_date, i.item_code, i.warehouse,
                   i.qty, i.delivered_qty, i.rate, i.conversion_factor, i.billed_amt, i.idx
            FROM `tabSales Order` o JOIN `tabSales Order Item` i ON i.parent = o.name
            WHERE o.company = @p AND o.docstatus = 1 AND o.status NOT IN ('Completed', 'Closed', 'Cancelled') AND o.transaction_date <= @asOf
            ORDER BY 1, 2, 13
            """, company,
            r => new ErpOrderLine(r.GetString(0), r.GetString(1), r.GetString(2), DateOnly.FromDateTime(r.GetDateTime(3)),
                r.IsDBNull(4) ? null : DateOnly.FromDateTime(r.GetDateTime(4)), r.GetString(5), Text(r, 6), r.GetDecimal(7), r.GetDecimal(8), r.GetDecimal(9),
                r.IsDBNull(10) || r.GetDecimal(10) == 0m ? 1m : r.GetDecimal(10), r.GetDecimal(11)),
            cancellationToken, ("@asOf", asOf.ToDateTime(TimeOnly.MinValue)));

    public Task<IReadOnlyList<ErpLeaveBalance>> LeaveBalancesAsync(string company, DateOnly asOf, CancellationToken cancellationToken) =>
        QueryAsync("""
            SELECT employee, leave_type, SUM(leaves),
                   MAX(transaction_type = 'Leave Allocation' AND is_expired = 0 AND leaves > 0 AND to_date >= @asOf)
            FROM `tabLeave Ledger Entry`
            WHERE company = @p AND docstatus = 1 AND from_date <= @asOf
            GROUP BY employee, leave_type
            ORDER BY employee, leave_type
            """, company,
            r => new ErpLeaveBalance(r.GetString(0), r.GetString(1), r.GetDecimal(2), r.GetInt64(3) == 1),
            cancellationToken, ("@asOf", asOf.ToDateTime(TimeOnly.MinValue)));

    private const string DetailColumns = """
        d.parent, d.parentfield = 'deductions', d.salary_component, d.abbr, CASE WHEN d.amount_based_on_formula = 1 THEN d.formula END, d.amount, d.`condition`,
        d.is_tax_applicable, d.depends_on_payment_days, d.statistical_component, d.do_not_include_in_total, d.variable_based_on_taxable_salary,
        d.additional_amount, d.additional_salary IS NOT NULL AND d.additional_salary <> ''
        """;

    private static ErpSalaryDetail Detail(MySqlDataReader r) =>
        new(r.GetString(0), r.GetInt64(1) == 1, r.GetString(2), r.GetString(3), Text(r, 4), r.GetDecimal(5), Text(r, 6), r.GetInt32(7) == 1, r.GetInt32(8) == 1,
            r.GetInt32(9) == 1, r.GetInt32(10) == 1, r.GetInt32(11) == 1, r.IsDBNull(12) ? 0m : r.GetDecimal(12), r.GetInt64(13) == 1);

    public async Task<IReadOnlyList<ErpSalaryStructure>> SalaryStructuresAsync(string company, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync($"""
            SELECT {DetailColumns}
            FROM `tabSalary Detail` d JOIN `tabSalary Structure` s ON s.name = d.parent
            WHERE d.parenttype = 'Salary Structure' AND s.company = @p AND s.docstatus = 1
              AND (s.is_active = 'Yes' OR s.name IN (SELECT salary_structure FROM `tabSalary Structure Assignment` WHERE company = @p AND docstatus = 1))
            ORDER BY d.parent, d.parentfield = 'deductions', d.idx
            """, company, Detail, cancellationToken).ConfigureAwait(false);
        return rows.GroupBy(d => d.Parent, StringComparer.Ordinal).Select(g => new ErpSalaryStructure(g.Key, g.ToList())).ToList();
    }

    public Task<IReadOnlyList<ErpAssignment>> AssignmentsAsync(string company, CancellationToken cancellationToken) =>
        QueryAsync("""
            SELECT employee, salary_structure, base, from_date, creation, name FROM `tabSalary Structure Assignment`
            WHERE company = @p AND docstatus = 1 ORDER BY employee, from_date, creation
            """, company,
            r => new ErpAssignment(r.GetString(0), r.GetString(1), r.GetDecimal(2), DateOnly.FromDateTime(r.GetDateTime(3)), r.GetDateTime(4), r.GetString(5)), cancellationToken);

    public async Task<ErpFormulaScope> FormulaScopeAsync(CancellationToken cancellationToken)
    {
        async Task<IReadOnlySet<string>> Columns(string table) =>
            (await QueryAsync("SELECT column_name FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = @p", table,
                r => r.GetString(0), cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        var abbreviations = (await QueryAsync("SELECT salary_component_abbr FROM `tabSalary Component` WHERE salary_component_abbr IS NOT NULL", null,
            r => r.GetString(0), cancellationToken).ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        return new ErpFormulaScope(await Columns("tabSalary Structure Assignment").ConfigureAwait(false), await Columns("tabEmployee").ConfigureAwait(false),
            await Columns("tabSalary Slip").ConfigureAwait(false), abbreviations);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>> FieldValuesAsync(string doctype, string company, IReadOnlyCollection<string> fields,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var result = new Dictionary<string, IReadOnlyDictionary<string, string?>>(StringComparer.Ordinal);
        if (fields.Count == 0)
        {
            return result;
        }

        var table = doctype switch
        {
            "Employee" => "tabEmployee",
            "Salary Structure Assignment" => "`tabSalary Structure Assignment`",
            _ => throw new ArgumentOutOfRangeException(nameof(doctype), doctype, null),
        };
        if (fields.Any(f => !f.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')))
        {
            throw new ArgumentException("Field names are identifiers taken from formulas and checked against the table's columns.", nameof(fields));
        }

        var list = fields.ToList();
        var rows = await QueryAsync($"SELECT name, {string.Join(", ", list.Select(f => $"CAST(`{f}` AS CHAR)"))} FROM {table} WHERE company = @p", company,
            r => (r.GetString(0), Enumerable.Range(0, list.Count).ToDictionary(i => list[i], i => Text(r, i + 1), StringComparer.Ordinal)), cancellationToken).ConfigureAwait(false);
        foreach (var (name, values) in rows)
        {
            result[name] = values;
        }

        return result;
    }

    public Task<IReadOnlyList<ErpSalarySlip>> LatestSlipsAsync(string company, DateOnly? asOf, CancellationToken cancellationToken) =>
        SlipsWhereAsync(company, """
            s.end_date = (SELECT MAX(end_date) FROM `tabSalary Slip` WHERE company = @p AND docstatus = 1 AND end_date <= @to)
            """, null, asOf ?? DateOnly.MaxValue, cancellationToken);

    public Task<IReadOnlyList<ErpSalarySlip>> SlipsAsync(string company, DateOnly? from, DateOnly to, CancellationToken cancellationToken) =>
        SlipsWhereAsync(company, "s.end_date <= @to AND (@from IS NULL OR s.start_date >= @from)", from, to, cancellationToken);

    private async Task<IReadOnlyList<ErpSalarySlip>> SlipsWhereAsync(string company, string filter, DateOnly? from, DateOnly to, CancellationToken cancellationToken)
    {
        var where = $"s.company = @p AND s.docstatus = 1 AND {filter}";
        (string, object)[] parameters = [("@to", to.ToDateTime(TimeOnly.MinValue)), ("@from", from is { } f ? f.ToDateTime(TimeOnly.MinValue) : DBNull.Value)];
        var details = (await QueryAsync(
                $"SELECT {DetailColumns} FROM `tabSalary Detail` d JOIN `tabSalary Slip` s ON s.name = d.parent AND d.parenttype = 'Salary Slip' WHERE {where} ORDER BY d.parent, d.idx",
                company, Detail, cancellationToken, parameters).ConfigureAwait(false))
            .ToLookup(d => d.Parent, StringComparer.Ordinal);
        return await QueryAsync(
            $"SELECT s.name, s.employee, s.salary_structure, s.start_date, s.end_date, s.payment_days, s.total_working_days, s.gross_pay, s.net_pay, s.creation FROM `tabSalary Slip` s WHERE {where} ORDER BY s.start_date, s.employee",
            company,
            r => new ErpSalarySlip(r.GetString(0), r.GetString(1), r.GetString(2), DateOnly.FromDateTime(r.GetDateTime(3)), DateOnly.FromDateTime(r.GetDateTime(4)),
                r.GetDecimal(5), r.GetDecimal(6), r.GetDecimal(7), r.GetDecimal(8), details[r.GetString(0)].ToList(), r.GetDateTime(9)),
            cancellationToken, parameters).ConfigureAwait(false);
    }

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
