namespace Gebeta.SharedKernel.Ethiopia;

/// <summary>
/// The Ethiopian government fiscal year: Hamle 1 to Sene 30 (about 8 July to 7 July).
/// It is named after the Ethiopian year in which it ends: FY 2018 runs from Hamle 1, 2017
/// to Sene 30, 2018 (8 July 2025 to 7 July 2026). Companies may use another fiscal year;
/// this is the default for Ethiopian companies and public enterprises.
/// </summary>
public readonly record struct EthiopianFiscalYear
{
    public EthiopianFiscalYear(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2);
        Year = year;
    }

    /// <summary>The Ethiopian year in which the fiscal year ends.</summary>
    public int Year { get; }

    public EthiopianDate Start => new(Year - 1, (int)EthiopianMonth.Hamle, 1);
    public EthiopianDate End => new(Year, (int)EthiopianMonth.Sene, 30);

    public DateOnly StartDate => Start.ToGregorian();
    public DateOnly EndDate => End.ToGregorian();

    public bool Contains(DateOnly date) => date >= StartDate && date <= EndDate;

    public static EthiopianFiscalYear Containing(DateOnly date)
    {
        var ethiopian = EthiopianDate.FromGregorian(date);
        return new EthiopianFiscalYear(ethiopian.Month >= (int)EthiopianMonth.Hamle ? ethiopian.Year + 1 : ethiopian.Year);
    }

    /// <summary>
    /// The 12 monthly periods (Hamle … Sene). Nehase also takes the following Pagume days,
    /// as Ethiopian payroll and accounting usually do.
    /// </summary>
    public IReadOnlyList<(EthiopianMonth Month, DateOnly From, DateOnly To)> MonthlyPeriods()
    {
        var periods = new List<(EthiopianMonth, DateOnly, DateOnly)>(12);
        var startYear = Year - 1;
        foreach (var month in new[] { EthiopianMonth.Hamle, EthiopianMonth.Nehase })
        {
            var from = new EthiopianDate(startYear, (int)month, 1).ToGregorian();
            var to = month == EthiopianMonth.Nehase
                ? new EthiopianDate(startYear, 13, EthiopianDate.DaysInMonth(startYear, 13)).ToGregorian()
                : new EthiopianDate(startYear, (int)month, 30).ToGregorian();
            periods.Add((month, from, to));
        }

        for (var m = 1; m <= 10; m++)
        {
            periods.Add(((EthiopianMonth)m, new EthiopianDate(Year, m, 1).ToGregorian(), new EthiopianDate(Year, m, 30).ToGregorian()));
        }

        return periods;
    }

    public override string ToString() => $"FY {Year} E.C.";
}
