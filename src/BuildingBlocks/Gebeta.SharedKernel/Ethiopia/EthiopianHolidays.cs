namespace Gebeta.SharedKernel.Ethiopia;

public enum HolidayKind
{
    /// <summary>Same Ethiopian (or Gregorian) date every year.</summary>
    Fixed,

    /// <summary>Follows Orthodox Easter (Siklet, Fasika).</summary>
    Movable,

    /// <summary>Follows the Islamic lunar calendar; the date is announced each year and entered by hand.</summary>
    Announced,
}

public sealed record PublicHoliday(DateOnly Date, string Name, string AmharicName, HolidayKind Kind);

/// <summary>
/// Ethiopian national public holidays (docs/ethiopia.md). Fixed and Orthodox holidays are computed;
/// Eid al-Fitr, Eid al-Adha and Mawlid depend on the moon and are announced each year, so they are
/// not computed here and must be added as announced holidays.
/// </summary>
public static class EthiopianHolidays
{
    /// <summary>Names of the holidays whose date must be entered every year when announced.</summary>
    public static IReadOnlyList<(string Name, string AmharicName)> AnnouncedEachYear { get; } =
    [
        ("Eid al-Fitr", "ኢድ አልፈጥር"),
        ("Eid al-Adha", "ኢድ አልአድሓ (አረፋ)"),
        ("Mawlid", "መውሊድ"),
    ];

    /// <summary>The computed holidays that fall in Ethiopian year <paramref name="ethiopianYear"/> (Meskerem 1 – Pagume).</summary>
    public static IReadOnlyList<PublicHoliday> ForEthiopianYear(int ethiopianYear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(ethiopianYear, 1900);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(ethiopianYear, 2091); // Orthodox Easter rule below holds until 2099 G.C.

        DateOnly Et(int month, int day) => new EthiopianDate(ethiopianYear, month, day).ToGregorian();

        // Genna is celebrated on Tahsas 29, or Tahsas 28 when the previous year was a leap year,
        // which keeps it on 7 January.
        var gennaDay = EthiopianDate.IsLeapYear(ethiopianYear - 1) ? 28 : 29;

        // Fasika falls in the second half of the Ethiopian year: Gregorian year = Ethiopian year + 8.
        var fasika = OrthodoxEaster(ethiopianYear + 8);

        var holidays = new List<PublicHoliday>
        {
            new(Et(1, 1), "Enkutatash (Ethiopian New Year)", "እንቁጣጣሽ", HolidayKind.Fixed),
            new(Et(1, 17), "Meskel (Finding of the True Cross)", "መስቀል", HolidayKind.Fixed),
            new(Et(4, gennaDay), "Genna (Ethiopian Christmas)", "ገና", HolidayKind.Fixed),
            new(Et(5, 11), "Timket (Epiphany)", "ጥምቀት", HolidayKind.Fixed),
            new(Et(6, 23), "Adwa Victory Day", "የዓድዋ ድል በዓል", HolidayKind.Fixed),
            new(fasika.AddDays(-2), "Siklet (Good Friday)", "ስቅለት", HolidayKind.Movable),
            new(fasika, "Fasika (Ethiopian Easter)", "ፋሲካ", HolidayKind.Movable),
            new(new DateOnly(ethiopianYear + 8, 5, 1), "International Labour Day", "የዓለም የሠራተኞች ቀን", HolidayKind.Fixed),
            new(Et(8, 27), "Patriots' Victory Day", "የአርበኞች ድል በዓል", HolidayKind.Fixed),
            new(Et(9, 20), "Downfall of the Derg", "ደርግ የወደቀበት ቀን", HolidayKind.Fixed),
        };

        return holidays.OrderBy(h => h.Date).ToList();
    }

    /// <summary>Holidays computed for the Ethiopian years that overlap Gregorian <paramref name="year"/>, limited to that year.</summary>
    public static IReadOnlyList<PublicHoliday> ForGregorianYear(int year) =>
        ForEthiopianYear(year - 8).Concat(ForEthiopianYear(year - 7))
            .Where(h => h.Date.Year == year)
            .OrderBy(h => h.Date)
            .ToList();

    /// <summary>
    /// Orthodox (Julian computus) Easter Sunday as a Gregorian date, valid 1900–2099.
    /// The Ethiopian Orthodox Church celebrates Fasika on this day.
    /// </summary>
    public static DateOnly OrthodoxEaster(int gregorianYear)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(gregorianYear, 1900);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gregorianYear, 2099);

        var a = gregorianYear % 4;
        var b = gregorianYear % 7;
        var c = gregorianYear % 19;
        var d = (19 * c + 15) % 30;
        var e = (2 * a + 4 * b - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = (d + e + 114) % 31 + 1;

        // Julian calendar date → Gregorian: 13 days ahead between 1900 and 2099.
        return new DateOnly(gregorianYear, month, day).AddDays(13);
    }
}
