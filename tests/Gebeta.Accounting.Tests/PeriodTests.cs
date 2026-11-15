using Gebeta.Accounting.Domain.Ledger;
using Gebeta.SharedKernel;

namespace Gebeta.Accounting.Tests;

public class PeriodTests
{
    private static readonly Guid Company = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 12, 10, 9, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public void An_ethiopian_period_is_the_ethiopian_month()
    {
        var hidar = AccountingPeriod.Containing(new DateOnly(2026, 11, 15), ethiopian: true);
        Assert.Equal(new AccountingPeriod(new DateOnly(2026, 11, 10), new DateOnly(2026, 12, 9), "Hidar 2019"), hidar);
    }

    [Fact]
    public void Pagume_belongs_to_the_nehase_period()
    {
        var nehase = AccountingPeriod.Containing(new DateOnly(2026, 9, 8), ethiopian: true); // Pagume 3, 2018
        Assert.Equal("Nehase 2018", nehase.Label);
        Assert.Equal(new DateOnly(2026, 8, 7), nehase.Start);  // Nehase 1, 2018
        Assert.Equal(new DateOnly(2026, 9, 10), nehase.End);   // Pagume 5, 2018
        Assert.Equal(nehase, AccountingPeriod.Containing(new DateOnly(2026, 8, 20), ethiopian: true));
    }

    [Fact]
    public void A_gregorian_period_is_the_calendar_month()
    {
        var november = AccountingPeriod.Containing(new DateOnly(2026, 11, 15), ethiopian: false);
        Assert.Equal(new AccountingPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), "November 2026"), november);
    }

    [Fact]
    public void Months_close_in_order_and_only_when_balanced()
    {
        var hidar = AccountingPeriod.Containing(new DateOnly(2026, 11, 15), true);
        var tahsas = AccountingPeriod.Containing(new DateOnly(2026, 12, 15), true);
        var tir = AccountingPeriod.Containing(new DateOnly(2027, 1, 15), true);

        Assert.Equal("accounting.unbalanced", Assert.Throws<DomainException>(() => PeriodClosing.Close(Company, hidar, null, 100m, 99m, 0m, "a", Now)).Code);
        var closed = PeriodClosing.Close(Company, hidar, null, 100m, 100m, 20m, "a", Now);
        Assert.Equal("accounting.period_already_closed", Assert.Throws<DomainException>(() => PeriodClosing.Close(Company, hidar, closed.End, 1m, 1m, 0m, "a", Now)).Code);
        Assert.Equal("accounting.period_order", Assert.Throws<DomainException>(() => PeriodClosing.Close(Company, tir, closed.End, 1m, 1m, 0m, "a", Now)).Code);
        Assert.Equal(PeriodClosingStatus.Closed, PeriodClosing.Close(Company, tahsas, closed.End, 1m, 1m, 0m, "a", Now).Status);
    }

    [Fact]
    public void Reopening_needs_a_reason()
    {
        var closing = PeriodClosing.Close(Company, AccountingPeriod.Containing(new DateOnly(2026, 11, 15), true), null, 0m, 0m, 0m, "a", Now);
        Assert.Equal("accounting.reopen_reason", Assert.Throws<DomainException>(() => closing.Reopen("b", Now, " ")).Code);

        closing.Reopen("b", Now, "Late supplier invoice");
        Assert.Equal(PeriodClosingStatus.Reopened, closing.Status);
        Assert.Equal("accounting.period_not_closed", Assert.Throws<DomainException>(() => closing.Reopen("b", Now, "again")).Code);
    }

    [Fact]
    public void Fiscal_years_follow_the_company_calendar()
    {
        Assert.Equal(new FiscalYearSpan(new DateOnly(2026, 7, 8), new DateOnly(2027, 7, 7), "FY 2019 E.C."), FiscalYearSpan.Containing(new DateOnly(2027, 3, 1), true));
        Assert.Equal(new FiscalYearSpan(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), "FY 2027"), FiscalYearSpan.Containing(new DateOnly(2027, 3, 1), false));
    }
}
