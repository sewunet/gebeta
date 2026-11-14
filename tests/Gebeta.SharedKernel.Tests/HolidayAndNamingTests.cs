using System.Globalization;
using Gebeta.SharedKernel.Ethiopia;
using Gebeta.SharedKernel.Naming;

namespace Gebeta.SharedKernel.Tests;

public class EthiopianHolidayTests
{
    private static DateOnly D(string s) => DateOnly.Parse(s, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(2024, "2024-05-05")]
    [InlineData(2025, "2025-04-20")]
    [InlineData(2026, "2026-04-12")]
    public void Orthodox_easter_matches_known_fasika_dates(int year, string expected)
    {
        Assert.Equal(D(expected), EthiopianHolidays.OrthodoxEaster(year));
    }

    [Fact]
    public void Gregorian_2025_has_the_ten_computed_national_holidays()
    {
        var holidays = EthiopianHolidays.ForGregorianYear(2025).ToDictionary(h => h.Name.Split(' ')[0], h => h.Date);

        Assert.Equal(10, holidays.Count);
        Assert.Equal(D("2025-01-07"), holidays["Genna"]);
        Assert.Equal(D("2025-01-19"), holidays["Timket"]);
        Assert.Equal(D("2025-03-02"), holidays["Adwa"]);
        Assert.Equal(D("2025-04-18"), holidays["Siklet"]);
        Assert.Equal(D("2025-04-20"), holidays["Fasika"]);
        Assert.Equal(D("2025-05-01"), holidays["International"]);
        Assert.Equal(D("2025-05-05"), holidays["Patriots'"]);
        Assert.Equal(D("2025-05-28"), holidays["Downfall"]);
        Assert.Equal(D("2025-09-11"), holidays["Enkutatash"]);
        Assert.Equal(D("2025-09-27"), holidays["Meskel"]);
    }

    [Theory]
    [InlineData(2016, "2024-01-07")] // after the leap year 2015: Tahsas 28
    [InlineData(2017, "2025-01-07")]
    [InlineData(2020, "2028-01-07")] // after the leap year 2019
    public void Genna_stays_on_7_january(int ethiopianYear, string expected)
    {
        var genna = EthiopianHolidays.ForEthiopianYear(ethiopianYear).Single(h => h.Name.StartsWith("Genna", StringComparison.Ordinal));

        Assert.Equal(D(expected), genna.Date);
    }

    [Fact]
    public void Every_holiday_has_an_amharic_name_and_islamic_holidays_are_announced()
    {
        Assert.All(EthiopianHolidays.ForEthiopianYear(2019), h => Assert.False(string.IsNullOrWhiteSpace(h.AmharicName)));
        Assert.Equal(3, EthiopianHolidays.AnnouncedEachYear.Count);
    }
}

public class NamingPatternTests
{
    private static readonly DateOnly Nov14 = new(2026, 11, 14); // Hidar 5, 2019 E.C., FY 2019

    [Theory]
    [InlineData("PCR-.YYYY.-.#####", 12, "PCR-2026-00012")]
    [InlineData("INV-.ABBR.-.FY.-.####", 1, "INV-ESIG-2019-0001")]
    [InlineData("SAL-.EYYYY.-.EMM.-.###", 7, "SAL-2019-03-007")]
    [InlineData("JV-.YY..MM.-.#####", 42, "JV-2611-00042")]
    [InlineData("WHT/.FY./.######", 3, "WHT/2019/000003")]
    [InlineData("REC-.#####.-ET", 5, "REC-00005-ET")]
    public void Formats_numbers(string pattern, long number, string expected)
    {
        Assert.Equal(expected, NamingPattern.Parse(pattern).Value.Format(Nov14, "ESIG", number));
    }

    [Fact]
    public void The_prefix_changes_with_the_fiscal_year_so_numbering_restarts()
    {
        var pattern = NamingPattern.Parse("INV-.FY.-.#####").Value;

        Assert.Equal("INV-2018-", pattern.PrefixFor(new DateOnly(2026, 7, 7), "X"));
        Assert.Equal("INV-2019-", pattern.PrefixFor(new DateOnly(2026, 7, 8), "X"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("PCR-.YYYY.")]
    [InlineData("PCR-.##.")]
    [InlineData("PCR-.###.-.###")]
    [InlineData("PCR#-.#####")]
    public void Rejects_invalid_patterns(string pattern)
    {
        Assert.True(NamingPattern.Parse(pattern).IsFailure);
    }
}
