using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Accounting.Domain.Ledger;

/// <summary>What a financial report is compared with.</summary>
public enum ReportComparison
{
    None,

    /// <summary>The period(s) just before (the same number of whole periods, or of days for any other range).</summary>
    PreviousPeriod,

    /// <summary>The same span one year earlier (Ethiopian or Gregorian year, as the company's fiscal year).</summary>
    LastYear,

    /// <summary>Profit and loss only: fiscal year to date against the same span of the previous fiscal year.</summary>
    LastYearToDate,
}

/// <summary>
/// The dates a report is compared with, from the company's fiscal calendar: an accounting period is an Ethiopian month (Nehase takes
/// Pagume) or a Gregorian month. A range that ends on a period's last day keeps ending on one, so Nehase with Pagume compares with
/// last year's Nehase with its own Pagume, whatever its length. Pure dates; no data is read.
/// </summary>
public static class ComparisonPeriods
{
    /// <summary>A profit and loss range and what it is compared with. For <see cref="ReportComparison.LastYearToDate"/> the current range becomes fiscal year to date.</summary>
    public static ((DateOnly From, DateOnly To) Current, (DateOnly From, DateOnly To) Comparison) ForRange(DateOnly from, DateOnly to, ReportComparison kind, bool ethiopian)
    {
        switch (kind)
        {
            case ReportComparison.PreviousPeriod when IsWholePeriods(from, to, ethiopian):
            {
                var periods = 1;
                for (var p = AccountingPeriod.Containing(from, ethiopian); p.End < to; p = AccountingPeriod.Containing(p.End.AddDays(1), ethiopian))
                {
                    periods++;
                }

                var start = AccountingPeriod.Containing(from.AddDays(-1), ethiopian);
                for (var i = 1; i < periods; i++)
                {
                    start = AccountingPeriod.Containing(start.Start.AddDays(-1), ethiopian);
                }

                return ((from, to), (start.Start, from.AddDays(-1)));
            }

            case ReportComparison.PreviousPeriod:
            {
                var days = to.DayNumber - from.DayNumber + 1;
                return ((from, to), (from.AddDays(-days), from.AddDays(-1)));
            }

            case ReportComparison.LastYear:
                return ((from, to), (StartYearEarlier(from, ethiopian), EndYearEarlier(to, ethiopian)));

            case ReportComparison.LastYearToDate:
            {
                var yearStart = FiscalYearSpan.Containing(to, ethiopian).Start;
                var lastTo = EndYearEarlier(to, ethiopian);
                return ((yearStart, to), (FiscalYearSpan.Containing(lastTo, ethiopian).Start, lastTo));
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Choose what to compare with.");
        }
    }

    /// <summary>A balance sheet date and the date it is compared with: the end of the previous period, or the same date a year earlier.</summary>
    public static DateOnly ForDate(DateOnly asOf, ReportComparison kind, bool ethiopian) => kind switch
    {
        ReportComparison.PreviousPeriod => AccountingPeriod.Containing(asOf, ethiopian).Start.AddDays(-1),
        ReportComparison.LastYear => EndYearEarlier(asOf, ethiopian),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "A balance sheet compares with the previous period or the same date last year."),
    };

    private static bool IsWholePeriods(DateOnly from, DateOnly to, bool ethiopian) =>
        AccountingPeriod.Containing(from, ethiopian).Start == from && AccountingPeriod.Containing(to, ethiopian).End == to;

    /// <summary>The same date one year earlier; the first day of a period stays a first day.</summary>
    private static DateOnly StartYearEarlier(DateOnly date, bool ethiopian)
    {
        var earlier = YearEarlier(date, ethiopian);
        return AccountingPeriod.Containing(date, ethiopian).Start == date ? AccountingPeriod.Containing(earlier, ethiopian).Start : earlier;
    }

    /// <summary>The same date one year earlier; the last day of a period stays a last day (Pagume's length varies).</summary>
    private static DateOnly EndYearEarlier(DateOnly date, bool ethiopian)
    {
        var earlier = YearEarlier(date, ethiopian);
        return AccountingPeriod.Containing(date, ethiopian).End == date ? AccountingPeriod.Containing(earlier, ethiopian).End : earlier;
    }

    private static DateOnly YearEarlier(DateOnly date, bool ethiopian)
    {
        if (!ethiopian)
        {
            return date.AddYears(-1);
        }

        var e = EthiopianDate.FromGregorian(date);
        return new EthiopianDate(e.Year - 1, e.Month, Math.Min(e.Day, EthiopianDate.DaysInMonth(e.Year - 1, e.Month))).ToGregorian();
    }
}
