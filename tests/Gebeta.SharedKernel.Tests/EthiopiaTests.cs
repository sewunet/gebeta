using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.SharedKernel.Tests;

public class EthiopianDateTests
{
    [Theory]
    [InlineData(2016, 1, 1, "2023-09-12")]   // Enkutatash after the leap year 2015
    [InlineData(2015, 13, 6, "2023-09-11")]  // Pagume 6 exists in a leap year
    [InlineData(2017, 1, 1, "2024-09-11")]
    [InlineData(2017, 6, 23, "2025-03-02")]  // Adwa Victory Day, Yekatit 23
    [InlineData(2017, 11, 1, "2025-07-08")]  // Hamle 1: start of FY 2018
    [InlineData(2018, 10, 30, "2026-07-07")] // Sene 30: end of FY 2018
    [InlineData(2019, 3, 5, "2026-11-14")]
    public void Converts_both_ways(int year, int month, int day, string gregorian)
    {
        var expected = DateOnly.Parse(gregorian, System.Globalization.CultureInfo.InvariantCulture);
        var ethiopian = new EthiopianDate(year, month, day);

        Assert.Equal(expected, ethiopian.ToGregorian());
        Assert.Equal(ethiopian, EthiopianDate.FromGregorian(expected));
    }

    [Fact]
    public void Round_trips_every_day_for_ten_years()
    {
        for (var date = new DateOnly(2020, 1, 1); date < new DateOnly(2030, 1, 1); date = date.AddDays(1))
        {
            Assert.Equal(date, EthiopianDate.FromGregorian(date).ToGregorian());
        }
    }

    [Fact]
    public void Pagume_has_six_days_only_in_a_leap_year()
    {
        Assert.True(EthiopianDate.IsLeapYear(2015));
        Assert.Equal(6, EthiopianDate.DaysInMonth(2015, 13));
        Assert.Equal(5, EthiopianDate.DaysInMonth(2016, 13));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2016, 13, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EthiopianDate(2016, 1, 31));
    }

    [Fact]
    public void Formats_numeric_english_and_amharic()
    {
        var date = new EthiopianDate(2019, 3, 5);

        Assert.Equal("05/03/2019", date.ToString());
        Assert.Equal("Hidar 5, 2019", date.ToLongString());
        Assert.Equal("ኅዳር 5 ቀን 2019", date.ToLongString(amharic: true));
    }
}

public class EthiopianFiscalYearTests
{
    [Fact]
    public void Runs_from_hamle_1_to_sene_30()
    {
        var fy = new EthiopianFiscalYear(2018);

        Assert.Equal(new DateOnly(2025, 7, 8), fy.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 7), fy.EndDate);
        Assert.Equal("FY 2018 E.C.", fy.ToString());
    }

    [Theory]
    [InlineData("2025-07-07", 2017)]
    [InlineData("2025-07-08", 2018)]
    [InlineData("2026-07-07", 2018)]
    [InlineData("2026-11-14", 2019)]
    public void Finds_the_fiscal_year_of_a_date(string date, int expected)
    {
        var fy = EthiopianFiscalYear.Containing(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, fy.Year);
    }

    [Fact]
    public void Has_twelve_monthly_periods_that_cover_the_year_without_gaps()
    {
        var fy = new EthiopianFiscalYear(2018);
        var periods = fy.MonthlyPeriods();

        Assert.Equal(12, periods.Count);
        Assert.Equal(EthiopianMonth.Hamle, periods[0].Month);
        Assert.Equal(EthiopianMonth.Sene, periods[^1].Month);
        Assert.Equal(fy.StartDate, periods[0].From);
        Assert.Equal(fy.EndDate, periods[^1].To);
        for (var i = 1; i < periods.Count; i++)
        {
            Assert.Equal(periods[i - 1].To.AddDays(1), periods[i].From);
        }

        // Nehase 2017 includes the 5 Pagume days: 35 days in total.
        Assert.Equal(35, periods[1].To.DayNumber - periods[1].From.DayNumber + 1);
    }
}

public class TinTests
{
    [Theory]
    [InlineData("0012345678")]
    [InlineData("001 234 5678")]
    [InlineData("001-234-5678")]
    public void Accepts_ten_digits(string input)
    {
        var result = TaxpayerIdentificationNumber.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal("0012345678", result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("00123A5678")]
    public void Rejects_anything_else(string? input)
    {
        var result = TaxpayerIdentificationNumber.Create(input);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }
}
