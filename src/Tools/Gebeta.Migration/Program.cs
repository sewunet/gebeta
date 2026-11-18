using Gebeta.Core.Domain.Companies;

namespace Gebeta.Migration;

// gebeta-migrate --company "Arjo Dedessa Sugar Factory" [--target-company <gebeta id>] [--tin 0100000011]
//                [--steps parties,items] [--dry-run]
//                [--cutover 2026-07-07 --account-map accounts.csv]   balances at the end of the cut-over day
// ERPnext:  --source "<MariaDB connection string>"  or environment ERPNEXT_DB
// Gebeta:   --target "<PostgreSQL connection string>" or environment ConnectionStrings__Main
// (A named entry class, not top-level statements: the API tests reference this project and the API's Program.)
internal static class MigrationProgram
{
    public static async Task<int> Main(string[] args)
    {
        var arguments = Args.Parse(args);
        var company = arguments.Get("company");
        var source = arguments.Get("source") ?? Environment.GetEnvironmentVariable("ERPNEXT_DB");
        var target = arguments.Get("target") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Main");
        if (company is null || source is null || target is null)
        {
            Console.Error.WriteLine("Usage: gebeta-migrate --company <ERPNext company> [--target-company <id>] [--tin <TIN>] [--legal-form <form>] [--steps parties,items,warehouses,employees,structures,assignments,history,payroll,leave,invoices,orders,stock,opening] [--history-from yyyy-MM-dd] [--cutover yyyy-MM-dd] [--account-map file.csv] [--dry-run]");
            Console.Error.WriteLine("       ERPNext source: --source or ERPNEXT_DB; Gebeta database: --target or ConnectionStrings__Main.");
            return 2;
        }

        DateOnly? cutover = null;
        if (arguments.Get("cutover") is { } day)
        {
            if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
            {
                Console.Error.WriteLine($"--cutover {day}: use yyyy-MM-dd (Gregorian), the last day kept in ERPNext.");
                return 2;
            }

            cutover = parsed;
        }

        DateOnly? historyFrom = null;
        if (arguments.Get("history-from") is { } fromText)
        {
            if (!DateOnly.TryParseExact(fromText, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedFrom))
            {
                Console.Error.WriteLine($"--history-from {fromText}: use yyyy-MM-dd (Gregorian).");
                return 2;
            }

            historyFrom = parsedFrom;
        }

        var options = new MigrationOptions(
            company,
            Guid.TryParse(arguments.Get("target-company"), out var targetCompany) ? targetCompany : null,
            arguments.Get("tin"),
            Enum.TryParse<LegalForm>(arguments.Get("legal-form"), ignoreCase: true, out var form) ? form : LegalForm.PrivateLimitedCompany,
            arguments.Has("dry-run"),
            arguments.Get("steps")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            cutover,
            arguments.Get("account-map"),
            HistoryFrom: historyFrom);

        await using var services = MigrationHost.Build(target);
        var run = new MigrationRun(services, new MariaDbErpNextSource(source), new IdMap(target), options);
        var reports = await run.RunAsync(CancellationToken.None);

        Console.WriteLine(options.DryRun ? $"Dry run for {company} (nothing written)" : $"Migration of {company}");
        foreach (var r in reports)
        {
            Console.WriteLine($"  {(r.Ok ? "OK  " : "FAIL")} {r.Name,-11} created {r.Created}, adopted {r.Adopted}, already migrated {r.AlreadyMigrated}" +
                (r.Check is null ? "" : $" · {r.Check}"));
            foreach (var w in r.Warnings)
            {
                Console.WriteLine($"         warning: {w}");
            }

            foreach (var f in r.Failures)
            {
                Console.WriteLine($"         failed:  {f}");
            }
        }

        return reports.All(r => r.Ok) ? 0 : 1;
    }
}

internal sealed class Args
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] args)
    {
        var result = new Args();
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = args[i][2..];
            var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : null;
            result._values[key] = value;
        }

        return result;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public bool Has(string key) => _values.ContainsKey(key);
}
