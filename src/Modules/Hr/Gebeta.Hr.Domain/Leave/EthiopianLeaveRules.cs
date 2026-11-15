using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Hr.Domain.Leave;

public enum LeaveType
{
    Annual,
    Sick,
    Maternity,
    Paternity,
    Marriage,
    Bereavement,
    Unpaid,
}

/// <summary>How the days of a leave are counted.</summary>
public enum DayCounting
{
    /// <summary>Working days: Sundays (weekly rest day) and public holidays are not counted.</summary>
    WorkingDays,

    /// <summary>Every calendar day (maternity and sick leave run continuously).</summary>
    CalendarDays,
}

/// <summary>One leave type's rules.</summary>
public sealed record LeaveRule(LeaveType Type, bool IsPaid, DayCounting Counting, decimal? MaximumDays, bool UsesBalance, string LegalBasis);

/// <summary>
/// Leave under the Labour Proclamation No. 1156/2019 (docs/ethiopia.md). VERIFY the figures with a labour
/// law expert before go-live; they are data here so a change is a code-free update later.
/// </summary>
public static class EthiopianLeaveRules
{
    public const decimal AnnualLeaveFirstYear = 16m;

    public static IReadOnlyDictionary<LeaveType, LeaveRule> Rules { get; } = new Dictionary<LeaveType, LeaveRule>
    {
        [LeaveType.Annual] = new(LeaveType.Annual, true, DayCounting.WorkingDays, null, true, "Art. 77: 16 working days, +1 per 2 further years"),
        [LeaveType.Sick] = new(LeaveType.Sick, true, DayCounting.CalendarDays, 180m, false, "Art. 85: 1 month full pay, 2 months half pay, 3 months unpaid"),
        [LeaveType.Maternity] = new(LeaveType.Maternity, true, DayCounting.CalendarDays, 120m, false, "Art. 88: 30 days before and 90 days after confinement"),
        [LeaveType.Paternity] = new(LeaveType.Paternity, true, DayCounting.WorkingDays, 3m, false, "Art. 81(2): 3 working days"),
        [LeaveType.Marriage] = new(LeaveType.Marriage, true, DayCounting.WorkingDays, 3m, false, "Art. 81(1)(a): 3 working days"),
        [LeaveType.Bereavement] = new(LeaveType.Bereavement, true, DayCounting.WorkingDays, 3m, false, "Art. 81(1)(b): 3 working days"),
        [LeaveType.Unpaid] = new(LeaveType.Unpaid, false, DayCounting.CalendarDays, null, false, "Art. 81(3): leave without pay by agreement"),
    };

    /// <summary>
    /// Annual leave for a leave year: 16 working days after one year of service, plus one day for every two
    /// further years; less than a year of service gives a proportional share (whole days, rounded down).
    /// </summary>
    public static decimal AnnualEntitlement(DateOnly dateOfJoining, DateOnly asOf)
    {
        if (asOf < dateOfJoining)
        {
            return 0m;
        }

        var months = ((asOf.Year - dateOfJoining.Year) * 12) + asOf.Month - dateOfJoining.Month - (asOf.Day < dateOfJoining.Day ? 1 : 0);
        var completedYears = months / 12;
        if (completedYears < 1)
        {
            return Math.Floor(AnnualLeaveFirstYear * months / 12m);
        }

        return AnnualLeaveFirstYear + ((completedYears - 1) / 2);
    }

    /// <summary>Days a leave takes, counted the way its type requires.</summary>
    public static decimal CountDays(LeaveType type, DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            return 0m;
        }

        if (Rules[type].Counting == DayCounting.CalendarDays)
        {
            return to.DayNumber - from.DayNumber + 1;
        }

        var holidays = HolidaysBetween(from, to);
        var days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Sunday && !holidays.Contains(d))
            {
                days++;
            }
        }

        return days;
    }

    /// <summary>
    /// Pay factor of each sick day by its position in the 12 months before it: day 1–30 full pay (1),
    /// day 31–90 half pay (0.5), day 91–180 unpaid (0).
    /// </summary>
    public static decimal SickPayFactor(int sickDayNumberInTwelveMonths) => sickDayNumberInTwelveMonths switch
    {
        <= 30 => 1m,
        <= 90 => 0.5m,
        _ => 0m,
    };

    private static HashSet<DateOnly> HolidaysBetween(DateOnly from, DateOnly to)
    {
        var set = new HashSet<DateOnly>();
        for (var year = from.Year; year <= to.Year; year++)
        {
            foreach (var holiday in EthiopianHolidays.ForGregorianYear(year))
            {
                set.Add(holiday.Date);
            }
        }

        return set;
    }
}
