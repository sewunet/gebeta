using Gebeta.Hr.Domain.Leave;
using Gebeta.SharedKernel;

namespace Gebeta.Hr.Tests;

public class LeaveRuleTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2025, 1, 1, 16)]    // 1 completed year at FY 2019 start (8 July 2026): 16 days
    [InlineData(2020, 7, 8, 18)]    // 6 years: 16 + (6 − 1) / 2 = 18
    [InlineData(2017, 7, 8, 20)]    // 9 years: 16 + 4 = 20
    [InlineData(2026, 3, 8, 5)]     // 4 months: 16 × 4 / 12 = 5.33 → 5
    [InlineData(2026, 8, 1, 0)]     // joined after the date
    public void Annual_leave_follows_article_77(int y, int m, int d, int expected)
    {
        Assert.Equal(expected, EthiopianLeaveRules.AnnualEntitlement(D(y, m, d), D(2026, 7, 8)));
    }

    [Fact]
    public void Working_days_skip_sundays_and_public_holidays()
    {
        Assert.Equal(6m, EthiopianLeaveRules.CountDays(LeaveType.Annual, D(2026, 11, 16), D(2026, 11, 22)));   // Mon–Sun
        Assert.Equal(3m, EthiopianLeaveRules.CountDays(LeaveType.Annual, D(2027, 1, 5), D(2027, 1, 8)));     // Genna on 7 January
        Assert.Equal(7m, EthiopianLeaveRules.CountDays(LeaveType.Sick, D(2026, 11, 16), D(2026, 11, 22)));     // calendar days
    }

    [Theory]
    [InlineData(1, "1")]
    [InlineData(30, "1")]
    [InlineData(31, "0.5")]
    [InlineData(90, "0.5")]
    [InlineData(91, "0")]
    public void Sick_leave_is_paid_in_full_then_half_then_not(int dayNumber, string factor)
    {
        Assert.Equal(decimal.Parse(factor, System.Globalization.CultureInfo.InvariantCulture), EthiopianLeaveRules.SickPayFactor(dayNumber));
    }

    [Fact]
    public void Leave_types_have_their_legal_limits()
    {
        var company = Guid.CreateVersion7();
        Assert.Throws<DomainException>(() => LeaveApplication.Create(company, Guid.CreateVersion7(), "T", LeaveType.Paternity, D(2026, 11, 16), D(2026, 11, 19), null, D(2026, 11, 1)));
        Assert.Equal(120m, LeaveApplication.Create(company, Guid.CreateVersion7(), "T", LeaveType.Maternity, D(2026, 11, 1), D(2027, 2, 28), null, D(2026, 11, 1)).Days);
        Assert.Throws<DomainException>(() => LeaveApplication.Create(company, Guid.CreateVersion7(), "T", LeaveType.Maternity, D(2026, 11, 1), D(2027, 3, 1), null, D(2026, 11, 1)));
        Assert.Throws<DomainException>(() => LeaveApplication.Create(company, Guid.CreateVersion7(), "T", LeaveType.Annual, D(2026, 11, 22), D(2026, 11, 22), null, D(2026, 11, 1))); // a Sunday
    }

    [Fact]
    public void Annual_leave_needs_enough_balance_and_writes_to_the_ledger()
    {
        var leave = LeaveApplication.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Almaz", LeaveType.Annual, D(2026, 11, 16), D(2026, 11, 20), null, D(2026, 11, 1));
        Assert.Equal(5m, leave.Days);
        Assert.Throws<DomainException>(() => leave.Approve("hr", balance: 4m));

        var entry = leave.Approve("hr", balance: 16m);
        Assert.Equal(-5m, entry.Days);
        Assert.Equal(DocStatus.Submitted, leave.DocStatus);

        var reversal = leave.CancelApproved();
        Assert.Equal(5m, reversal.Days);
    }

    [Fact]
    public void Unpaid_leave_needs_no_balance()
    {
        var leave = LeaveApplication.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Dawit", LeaveType.Unpaid, D(2026, 11, 9), D(2026, 11, 10), null, D(2026, 11, 1));

        leave.Approve("hr", balance: 0m);

        Assert.Equal(2m, leave.Days);
    }
}
