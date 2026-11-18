using Gebeta.Accounting.Domain.Ledger;

namespace Gebeta.Accounting.Tests;

/// <summary>The dates a financial report is compared with (docs/requirements/competitive-features.md #3).</summary>
public class ComparisonPeriodTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    // Ethiopian: Hidar 2019 (10 Nov – 9 Dec 2026) against Tikimt 2019 and against Hidar 2018.
    [InlineData("2026-11-10", "2026-12-09", "PreviousPeriod", true, "2026-11-10", "2026-12-09", "2026-10-11", "2026-11-09")]
    [InlineData("2026-11-10", "2026-12-09", "LastYear", true, "2026-11-10", "2026-12-09", "2025-11-10", "2025-12-09")]
    // Year to date: Hamle 1, 2018 – Hidar 30, 2019 against the same span of FY 2018.
    [InlineData("2026-11-10", "2026-12-09", "LastYearToDate", true, "2026-07-08", "2026-12-09", "2025-07-08", "2025-12-09")]
    // Meskerem 2019's previous period is Nehase 2018 with its Pagume (Pagume 2018 has 5 days: 7 Aug – 10 Sep 2026).
    [InlineData("2026-09-11", "2026-10-10", "PreviousPeriod", true, "2026-09-11", "2026-10-10", "2026-08-07", "2026-09-10")]
    // Nehase 2019 with its 6-day Pagume (2019 is a leap year: 7 Aug – 11 Sep 2027) against Nehase 2018 with 5 days (7 Aug – 10 Sep 2026).
    [InlineData("2027-08-07", "2027-09-11", "LastYear", true, "2027-08-07", "2027-09-11", "2026-08-07", "2026-09-10")]
    // Two whole Ethiopian months compare with the two before.
    [InlineData("2026-10-11", "2026-12-09", "PreviousPeriod", true, "2026-10-11", "2026-12-09", "2026-08-07", "2026-10-10")]
    // A range that is not whole periods: the same number of days just before.
    [InlineData("2026-11-15", "2026-11-24", "PreviousPeriod", true, "2026-11-15", "2026-11-24", "2026-11-05", "2026-11-14")]
    // Gregorian companies: calendar months.
    [InlineData("2026-11-01", "2026-11-30", "PreviousPeriod", false, "2026-11-01", "2026-11-30", "2026-10-01", "2026-10-31")]
    [InlineData("2024-02-01", "2024-02-29", "LastYear", false, "2024-02-01", "2024-02-29", "2023-02-01", "2023-02-28")]
    [InlineData("2026-11-01", "2026-11-30", "LastYearToDate", false, "2026-01-01", "2026-11-30", "2025-01-01", "2025-11-30")]
    public void Ranges(string from, string to, string kind, bool ethiopian, string curFrom, string curTo, string cmpFrom, string cmpTo)
    {
        var (current, comparison) = ComparisonPeriods.ForRange(D(from), D(to), Enum.Parse<ReportComparison>(kind), ethiopian);
        Assert.Equal((D(curFrom), D(curTo)), current);
        Assert.Equal((D(cmpFrom), D(cmpTo)), comparison);
    }

    [Theory]
    [InlineData("2026-12-09", "PreviousPeriod", true, "2026-11-09")] // end of Hidar → end of Tikimt
    [InlineData("2026-12-09", "LastYear", true, "2025-12-09")]
    [InlineData("2027-09-11", "LastYear", true, "2026-09-10")]       // end of Nehase with Pagume: 6 days in 2019, 5 in 2018
    [InlineData("2026-11-30", "PreviousPeriod", false, "2026-10-31")]
    public void Balance_sheet_dates(string asOf, string kind, bool ethiopian, string expected) =>
        Assert.Equal(D(expected), ComparisonPeriods.ForDate(D(asOf), Enum.Parse<ReportComparison>(kind), ethiopian));
}
