using System.Globalization;
using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Application;

public sealed record PayrollComparisonQuery(Guid RunId) : IQuery<PayrollComparisonDto>;

/// <summary>
/// A payroll run's slips against the previous month's submitted slips (history from before the cut-over included), before the run is
/// submitted (docs/requirements/competitive-features.md #1). Read-only and recalculated each time; a warning tool, never a block.
/// Everything is read in batches (the run's slips, last month's, their entries and the month's new assignments), never per employee.
/// </summary>
public sealed class PayrollComparisonHandler(
    IPayrollRunRepository runs,
    ISalarySlipRepository slips,
    IAdditionalPayRepository additionalPay,
    ISalaryStructureAssignmentRepository assignments,
    IEmployeeList employees,
    ICompanyAccess access,
    SlipFigures figures,
    PayrollOptions options) : IQueryHandler<PayrollComparisonQuery, PayrollComparisonDto>
{
    public async Task<Result<PayrollComparisonDto>> Handle(PayrollComparisonQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await runs.GetAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null || !await access.CanAccessAsync(run.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Payroll run", query.RunId);
        }

        // This month: the run's slips, not cancelled. Last month: submitted slips of the period ending the day before.
        var current = (await slips.GetManyAsync(run.SlipIds.ToList(), cancellationToken).ConfigureAwait(false))
            .Where(s => s.DocStatus != DocStatus.Cancelled).ToList();
        var previousEnd = run.PeriodStart.AddDays(-1);
        var previous = (await slips.ListAsync(run.CompanyId, previousEnd, previousEnd, cancellationToken).ConfigureAwait(false))
            .Where(s => s.DocStatus == DocStatus.Submitted && s.PeriodEnd == previousEnd).ToList();

        var entries = (await additionalPay.ForSlipsAsync(current.Select(s => s.Id).ToList(), cancellationToken).ConfigureAwait(false))
            .ToLookup(e => e.SalarySlipId!.Value);
        var newAssignments = (await assignments.StartingBetweenAsync(run.CompanyId, run.PeriodStart, run.PeriodEnd, cancellationToken).ConfigureAwait(false))
            .Select(a => a.EmployeeId).ToHashSet();
        var leaving = (await employees.ListAsync(run.CompanyId, cancellationToken).ConfigureAwait(false))
            .Where(e => e.DateOfLeaving is not null).ToDictionary(e => e.Id, e => e.DateOfLeaving!.Value);

        // Two slips of one employee in a month (a final settlement) count together.
        var now = current.GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var before = previous.GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());
        var hasPrevious = previous.Count > 0;
        var threshold = options.ComparisonThresholdPercent;

        var rows = new List<PayComparisonRow>();
        foreach (var employeeId in now.Keys.Union(before.Keys))
        {
            var mine = now.GetValueOrDefault(employeeId);
            var theirs = before.GetValueOrDefault(employeeId);
            var any = (mine ?? theirs)![0];
            var cur = mine is null ? null : Figures(mine);
            var prev = theirs is null ? null : Figures(theirs);
            var change = (cur?.Net ?? 0m) - (prev?.Net ?? 0m);
            decimal? percent = prev is { Net: > 0m } ? Math.Round(change / prev.Net * 100m, 1) : null;

            var status = !hasPrevious ? "New" : cur is null ? "Missing" : prev is null ? "New" : change == 0m && cur.Gross == prev.Gross ? "Same" : "Changed";
            var reasons = Reasons(mine, entries, prev, cur, newAssignments.Contains(employeeId), leaving.TryGetValue(employeeId, out var left) ? left : null);
            var flagged = hasPrevious && (status is "New" or "Missing" || (status == "Changed" && (percent is null || Math.Abs(percent.Value) > threshold)));
            rows.Add(new PayComparisonRow(employeeId, any.EmployeeNumber, any.EmployeeName, prev, cur, change, percent, status, flagged, reasons));
        }

        rows = rows.OrderByDescending(r => r.Flagged).ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal).ToList();
        return new PayrollComparisonDto(run.Id, run.PeriodStart, run.PeriodEnd, previousEnd, hasPrevious, threshold, rows,
            Totals(previous), Totals(current), rows.Count(r => r.Flagged));
    }

    private PayFigures Figures(IReadOnlyList<SalarySlip> month) => new(
        month.Sum(s => s.GrossPay), month.Sum(figures.IncomeTax), month.Sum(figures.EmployeePension), month.Sum(figures.EmployerPension),
        month.Sum(s => s.NetPay), month.Sum(s => s.PaymentDays));

    private PayTotals Totals(IReadOnlyList<SalarySlip> month) => new(
        month.Select(s => s.EmployeeId).Distinct().Count(), month.Sum(s => s.GrossPay), month.Sum(figures.IncomeTax), month.Sum(figures.EmployeePension),
        month.Sum(figures.EmployerPension), month.Sum(s => s.NetPay));

    /// <summary>The reasons the data can show; a change with none of them says so, for a person to check.</summary>
    private static List<string> Reasons(IReadOnlyList<SalarySlip>? mine, ILookup<Guid, AdditionalPayEntry> entries, PayFigures? prev, PayFigures? cur,
        bool newAssignment, DateOnly? left)
    {
        var reasons = new List<string>();
        foreach (var entry in (mine ?? []).SelectMany(s => entries[s.Id]))
        {
            var sign = entry.Type == ComponentType.Earning ? "+" : "−";
            reasons.Add(entry.SourceType == AdditionalSalary.Source
                ? $"{entry.Name} {entry.SourceNumber} {sign}{entry.Amount.ToString("N2", CultureInfo.InvariantCulture)}"
                : $"{entry.Name} ({entry.SourceNumber}) {sign}{entry.Amount.ToString("N2", CultureInfo.InvariantCulture)}");
        }

        if (prev is not null && cur is not null && prev.PaymentDays != cur.PaymentDays)
        {
            reasons.Add($"payment days {prev.PaymentDays:0.##} → {cur.PaymentDays:0.##}");
        }

        if (newAssignment)
        {
            reasons.Add("new salary assignment this month");
        }

        if (left is { } date)
        {
            reasons.Add($"left on {date:yyyy-MM-dd}");
        }

        if (reasons.Count == 0 && prev is not null && cur is not null && prev.Net != cur.Net)
        {
            reasons.Add("no recorded reason");
        }

        return reasons;
    }
}
