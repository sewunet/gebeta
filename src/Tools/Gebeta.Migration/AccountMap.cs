using System.Globalization;
using System.Text;
using Gebeta.Accounting.Domain.Ledger;

namespace Gebeta.Migration;

/// <summary>
/// Which Gebeta account each ERPNext account's balance goes to: a CSV the finance team reviews and signs off
/// (erpnext_account, root_type, account_type, balance, gebeta_code). The first run writes it with suggestions; a blank
/// code stops the opening balances until someone fills it in.
/// </summary>
public static class AccountMap
{
    private static readonly string[] Header = ["erpnext_account", "root_type", "account_type", "balance", "gebeta_code"];

    /// <summary>ERPNext account → Gebeta code (null where the code is blank).</summary>
    public static IReadOnlyDictionary<string, string?> Read(string path)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var fields in File.ReadLines(path).Skip(1).Where(l => !string.IsNullOrWhiteSpace(l)).Select(Split))
        {
            var code = fields.Count > 4 ? fields[4].Trim() : string.Empty;
            map[fields[0]] = code.Length == 0 ? null : code;
        }

        return map;
    }

    public static void Write(string path, IEnumerable<ErpAccountBalance> accounts, Func<ErpAccountBalance, string?> code)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(code);
        var text = new StringBuilder();
        text.AppendLine(string.Join(',', Header));
        foreach (var a in accounts)
        {
            text.AppendLine(string.Join(',', new[] { a.Account, a.RootType, a.AccountType ?? "", a.Balance.ToString("0.00", CultureInfo.InvariantCulture), code(a) ?? "" }.Select(Quote)));
        }

        File.WriteAllText(path, text.ToString());
    }

    /// <summary>
    /// A first guess from ERPNext's account type and name, to be reviewed. Balance-sheet accounts with no clear match stay blank on
    /// purpose: guessing where a loan or an asset goes is the accountant's decision.
    /// </summary>
    public static string? Suggest(ErpAccountBalance account, IReadOnlySet<string> gebetaCodes)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(gebetaCodes);
        if (account.AccountNumber is { } number && gebetaCodes.Contains(number))
        {
            return number;
        }

        var name = account.Account.ToUpperInvariant();
        var asset = account.RootType == "Asset";
        return account.AccountType switch
        {
            "Receivable" => "1200",
            "Payable" => "2100",
            "Stock" => "1300",
            "Bank" => EthiopianChartOfAccounts.Bank,
            "Cash" => EthiopianChartOfAccounts.CashOnHand,
            "Cost of Goods Sold" => EthiopianChartOfAccounts.CostOfSales,
            "Stock Adjustment" => "5100",
            "Stock Received But Not Billed" => EthiopianChartOfAccounts.StockReceivedNotBilled,
            "Fixed Asset" => "1500",
            "Temporary" => EthiopianChartOfAccounts.OpeningClearing,
            "Tax" when name.Contains("VAT", StringComparison.Ordinal) => asset ? "1250" : "2300",
            "Tax" when name.Contains("WITHHOLD", StringComparison.Ordinal) => asset ? "1260" : "2400",
            _ => account.RootType switch
            {
                "Equity" when name.Contains("RETAINED", StringComparison.Ordinal) => EthiopianChartOfAccounts.RetainedEarnings,
                "Equity" when name.Contains("CAPITAL", StringComparison.Ordinal) => "3100",
                "Income" when name.Contains("SALES", StringComparison.Ordinal) => "4100",
                "Income" when name.Contains("INTEREST", StringComparison.Ordinal) => EthiopianChartOfAccounts.InterestIncome,
                "Income" => "4200",
                "Expense" when name.Contains("SALAR", StringComparison.Ordinal) || name.Contains("WAGE", StringComparison.Ordinal) => EthiopianChartOfAccounts.SalaryExpense,
                "Expense" when name.Contains("RENT", StringComparison.Ordinal) => "6200",
                "Expense" when name.Contains("UTILIT", StringComparison.Ordinal) || name.Contains("ELECTRIC", StringComparison.Ordinal) => "6300",
                "Expense" when name.Contains("BANK CHARGE", StringComparison.Ordinal) => EthiopianChartOfAccounts.BankCharges,
                "Expense" => "6900",
                _ => null,
            },
        };
    }

    private static string Quote(string value) =>
        value.IndexOfAny([',', '"', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    private static List<string> Split(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
