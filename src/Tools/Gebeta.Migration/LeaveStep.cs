using Gebeta.Hr.Application;
using Gebeta.Hr.Contracts;
using Gebeta.Hr.Domain.Leave;

namespace Gebeta.Migration;

/// <summary>
/// Annual leave balances at the end of the cut-over day (docs/data-migration.md step 6). ERPNext sites often keep one leave type
/// per year ("Annual Leave 2016", "የአመት እረፍት 2016"); every type whose name says annual counts, and together they make the employee's
/// Gebeta annual balance. Other kinds of leave have no balance in Gebeta (the law sets their maximum), so they are reported, not brought.
/// </summary>
public sealed partial class MigrationRun
{
    private const string LeaveOpening = "Leave Opening";

    /// <summary>Annual leave, by its name in English or Amharic (including the usual misspelling).</summary>
    public static bool IsAnnualLeave(string leaveType)
    {
        ArgumentNullException.ThrowIfNull(leaveType);
        return leaveType.Contains("annual", StringComparison.OrdinalIgnoreCase) || leaveType.Contains("anual", StringComparison.OrdinalIgnoreCase)
            || leaveType.Contains("የአመት", StringComparison.Ordinal) || leaveType.Contains("የዓመት", StringComparison.Ordinal);
    }

    private async Task OpeningLeaveAsync(DateOnly cutover, StepReport report, CancellationToken cancellationToken)
    {
        var rows = await source.LeaveBalancesAsync(options.Company, cutover, cancellationToken).ConfigureAwait(false);
        var annual = rows.Where(r => IsAnnualLeave(r.LeaveType)).ToList();
        var counted = annual.Select(r => r.LeaveType).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (counted.Count > 0)
        {
            report.Warnings.Add($"Counted as annual leave: {string.Join(", ", counted)}.");
        }

        foreach (var other in rows.Except(annual).Where(r => r.Balance > 0m).GroupBy(r => r.LeaveType, StringComparer.Ordinal))
        {
            report.Warnings.Add($"{other.Key}: {other.Count()} employees hold {other.Sum(r => r.Balance):0.##} days; Gebeta keeps no balance for this kind of leave, so it is not brought.");
        }

        var employee = await ResolverAsync("Employee", cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync(LeaveOpening, cancellationToken).ConfigureAwait(false);
        var expected = new Dictionary<Guid, decimal>();
        var notMigrated = 0;
        foreach (var e in annual.GroupBy(r => r.Employee, StringComparer.Ordinal))
        {
            var days = e.Sum(r => r.Balance);
            var coversYear = e.Any(r => r.AllocationCoversDate);
            if (days == 0m && !coversYear)
            {
                continue; // nothing to bring, and Gebeta's yearly allocation gives this year
            }

            if (employee(e.Key) is not { } employeeId)
            {
                notMigrated++;
                continue;
            }

            if (days < 0m)
            {
                report.Failures.Add($"{e.Key}: annual leave balance {days:0.##} days (more taken than allocated); correct it in ERPNext.");
                continue;
            }

            var rounded = decimal.Round(days, 1, MidpointRounding.ToZero);
            if (rounded != days)
            {
                report.Warnings.Add($"{e.Key}: {days:0.###} days becomes {rounded:0.#} (Gebeta keeps tenths of a day).");
            }

            if (known.ContainsKey(e.Key))
            {
                report.AlreadyMigrated++;
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var recorded = await Handle<RecordOpeningLeaveCommand, LeaveBalanceDto>(new RecordOpeningLeaveCommand(employeeId, LeaveType.Annual, cutover, rounded, coversYear),
                cancellationToken).ConfigureAwait(false);
            if (recorded.IsFailure)
            {
                report.Failures.Add($"{e.Key}: {recorded.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, LeaveOpening, e.Key, employeeId, cancellationToken).ConfigureAwait(false);
            report.Created++;
            expected[employeeId] = rounded;
        }

        if (notMigrated > 0)
        {
            report.Warnings.Add($"{notMigrated} employees with annual leave are not migrated (left, or failed the employees step).");
        }

        if (options.DryRun)
        {
            report.Check = $"dry run: {report.Created} opening balances, {annual.Where(r => r.Balance > 0m).Sum(r => r.Balance):0.##} days";
            return;
        }

        // The proof: each balance recorded in this run is what ERPNext had (only this run's: later leave taken in Gebeta changes the
        // balance, and a rerun must not report that as a difference).
        var wrong = new List<string>();
        foreach (var (employeeId, days) in expected)
        {
            var balances = await Query<LeaveBalanceQuery, IReadOnlyList<LeaveBalanceDto>>(new LeaveBalanceQuery(employeeId), cancellationToken).ConfigureAwait(false);
            var balance = balances.IsSuccess ? balances.Value.FirstOrDefault(b => b.Type == nameof(LeaveType.Annual))?.Balance ?? 0m : 0m;
            if (balance != days)
            {
                wrong.Add($"{employeeId}: Gebeta {balance:0.#}, ERPNext {days:0.#}");
            }
        }

        report.Failures.AddRange(wrong);
        report.CheckPassed = wrong.Count == 0;
        report.Check = $"annual leave at {cutover:yyyy-MM-dd}: {report.AlreadyMigrated} already brought; this run {expected.Count - wrong.Count} of {expected.Count} balances equal ERPNext ({expected.Values.Sum():0.#} days)";
    }
}
