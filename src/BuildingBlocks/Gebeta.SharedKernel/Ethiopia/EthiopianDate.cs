using System.Globalization;

namespace Gebeta.SharedKernel.Ethiopia;

/// <summary>
/// A date in the Ethiopian calendar (Amete Mihret): 12 months of 30 days and Pagume of 5 days
/// (6 in a leap year, when <c>year % 4 == 3</c>). Gebeta stores Gregorian <see cref="DateOnly"/>;
/// this type is for display, input and Ethiopian fiscal periods (docs/ethiopia.md).
/// </summary>
public readonly record struct EthiopianDate : IComparable<EthiopianDate>
{
    // Julian Day Number of Meskerem 1, year 1 (Amete Mihret) minus one year, per the standard algorithm.
    private const int JdnEpochOffset = 1_723_856;

    // JDN of 0001-01-01 (Gregorian); DateOnly.DayNumber counts days from that date.
    private const int JdnOfDayNumberZero = 1_721_426;

    public EthiopianDate(int year, int month, int day)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(month, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(month, 13);
        ArgumentOutOfRangeException.ThrowIfLessThan(day, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(day, DaysInMonth(year, month));

        Year = year;
        Month = month;
        Day = day;
    }

    public int Year { get; }
    public int Month { get; }
    public int Day { get; }

    public EthiopianMonth MonthName => (EthiopianMonth)Month;

    public static bool IsLeapYear(int year) => year % 4 == 3;

    public static int DaysInMonth(int year, int month) => month switch
    {
        >= 1 and <= 12 => 30,
        13 => IsLeapYear(year) ? 6 : 5,
        _ => throw new ArgumentOutOfRangeException(nameof(month), "An Ethiopian month is 1 to 13."),
    };

    public static EthiopianDate FromGregorian(DateOnly date)
    {
        var jdn = date.DayNumber + JdnOfDayNumberZero;
        var r = Mod(jdn - JdnEpochOffset, 1461);
        var n = Mod(r, 365) + 365 * (r / 1460);
        var year = 4 * FloorDiv(jdn - JdnEpochOffset, 1461) + r / 365 - r / 1460;
        var month = n / 30 + 1;
        var day = Mod(n, 30) + 1;
        return new EthiopianDate(year, month, day);
    }

    public DateOnly ToGregorian()
    {
        var jdn = JdnEpochOffset + 365 + 365 * (Year - 1) + Year / 4 + 30 * Month + Day - 31;
        return DateOnly.FromDayNumber(jdn - JdnOfDayNumberZero);
    }

    public EthiopianDate AddDays(int days) => FromGregorian(ToGregorian().AddDays(days));

    public int CompareTo(EthiopianDate other) => ToGregorian().CompareTo(other.ToGregorian());

    public static bool operator <(EthiopianDate left, EthiopianDate right) => left.CompareTo(right) < 0;
    public static bool operator >(EthiopianDate left, EthiopianDate right) => left.CompareTo(right) > 0;
    public static bool operator <=(EthiopianDate left, EthiopianDate right) => left.CompareTo(right) <= 0;
    public static bool operator >=(EthiopianDate left, EthiopianDate right) => left.CompareTo(right) >= 0;

    /// <summary>Numeric form used on Ethiopian documents: dd/MM/yyyy, e.g. 05/03/2019.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Day:00}/{Month:00}/{Year:0000}");

    /// <summary>Long form with the month name, e.g. "Hidar 5, 2019" or "ኅዳር 5 ቀን 2019".</summary>
    public string ToLongString(bool amharic = false) => amharic
        ? string.Create(CultureInfo.InvariantCulture, $"{EthiopianMonthNames.Amharic(MonthName)} {Day} ቀን {Year}")
        : string.Create(CultureInfo.InvariantCulture, $"{EthiopianMonthNames.English(MonthName)} {Day}, {Year}");

    private static int Mod(int a, int b) => ((a % b) + b) % b;

    private static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);
}

public enum EthiopianMonth
{
    Meskerem = 1,
    Tikimt = 2,
    Hidar = 3,
    Tahsas = 4,
    Tir = 5,
    Yekatit = 6,
    Megabit = 7,
    Miyazya = 8,
    Ginbot = 9,
    Sene = 10,
    Hamle = 11,
    Nehase = 12,
    Pagume = 13,
}

public static class EthiopianMonthNames
{
    private static readonly string[] AmharicNames =
        ["መስከረም", "ጥቅምት", "ኅዳር", "ታኅሣሥ", "ጥር", "የካቲት", "መጋቢት", "ሚያዝያ", "ግንቦት", "ሰኔ", "ሐምሌ", "ነሐሴ", "ጳጉሜን"];

    public static string English(EthiopianMonth month) => month.ToString();

    public static string Amharic(EthiopianMonth month) => AmharicNames[(int)month - 1];
}
