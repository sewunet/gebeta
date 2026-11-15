using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;

namespace Gebeta.Payroll.Tests;

/// <summary>The demo cases of the current customized_forcommon Salary Back Payment, recalculated in Gebeta.</summary>
public class BackPaymentTests
{
    private static readonly SalaryStructure Standard = SalaryStructure.Create("Standard", EthiopianSalaryStructures.Standard).Value;

    /// <summary>Months paid at <paramref name="oldBase"/>, as their submitted slips would show.</summary>
    private static List<MonthBaseline> PaidMonths(decimal oldBase, params (int Year, int Month)[] months) => months.Select(m =>
    {
        var start = new DateOnly(m.Year, m.Month, 1);
        var end = start.AddMonths(1).AddDays(-1);
        var slip = SalaryCalculator.Calculate(Standard, new SalaryInput(oldBase, 30, 30, end), EthiopianIncomeTax.ValidOn(end));
        return new MonthBaseline(start, end, $"SAL-{m.Month}", 30, 30, slip.GrossPay, slip.TaxableEarnings,
            slip.Lines.Single(l => l.Abbreviation == "IT").Amount, slip.Lines.Single(l => l.Abbreviation == "PE").Amount, slip.EmployerContributions);
    }).ToList();

    private static SalaryBackPayment Create(decimal newBase, List<MonthBaseline> months)
    {
        var calculated = BackPaymentCalculator.Calculate(Standard, newBase, months, EthiopianIncomeTax.ValidOn);
        return SalaryBackPayment.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "EMP-1", "Test", Guid.CreateVersion7(), newBase,
            months[0].PeriodStart, months[^1].PeriodEnd, new DateOnly(2026, 11, 25), "Increment approved late", "HR/INC/2026/001", calculated);
    }

    [Fact]
    public void An_increment_pays_arrears_with_tax_in_the_months_it_belongs_to()
    {
        // Abebe: 12,000 → 13,000 for August–October 2026.
        var backPayment = Create(13_000m, PaidMonths(12_000m, (2026, 8), (2026, 9), (2026, 10)));

        Assert.All(backPayment.Months, m =>
        {
            Assert.Equal(1_000m, m.GrossArrears);
            Assert.Equal(300m, m.AdditionalTax);           // 2,550 − 2,250 per month
            Assert.Equal(70m, m.OtherDeductionsArrears);    // 7% pension on 1,000
            Assert.Equal(110m, m.EmployerContributionsArrears);
            Assert.Equal(630m, m.NetArrears);
        });
        Assert.Equal(3_000m, backPayment.TotalGrossArrears);
        Assert.Equal(900m, backPayment.TotalAdditionalTax);
        Assert.Equal(1_890m, backPayment.NetArrears);
    }

    [Fact]
    public void Tax_per_month_is_less_than_taxing_all_arrears_in_one_month()
    {
        var backPayment = Create(13_000m, PaidMonths(12_000m, (2026, 8), (2026, 9), (2026, 10)));

        // Putting 3,000 of arrears on top of November's 13,000 would cost 16,000 → 3,550 tax, i.e. 1,000 extra.
        var lumpSumExtraTax = EthiopianIncomeTax.Current.Calculate(16_000m) - EthiopianIncomeTax.Current.Calculate(13_000m);
        Assert.Equal(1_000m, lumpSumExtraTax);
        Assert.True(backPayment.TotalAdditionalTax < lumpSumExtraTax);
    }

    [Fact]
    public void A_low_salary_crossing_the_tax_free_threshold_gets_its_first_tax()
    {
        // Dawit: 1,800 (tax free) → 2,200.
        var backPayment = Create(2_200m, PaidMonths(1_800m, (2026, 8), (2026, 9), (2026, 10)));

        Assert.All(backPayment.Months, m => Assert.Equal(30m, m.AdditionalTax));
        Assert.Equal(1_200m, backPayment.TotalGrossArrears);
        Assert.Equal(1_026m, backPayment.NetArrears);
    }

    [Fact]
    public void A_downgrade_becomes_a_recovery_with_tax_and_pension_refunds()
    {
        // Hirut: 9,000 → 8,500 for September–October 2026.
        var backPayment = Create(8_500m, PaidMonths(9_000m, (2026, 9), (2026, 10)));
        Assert.Equal(-680m, backPayment.NetArrears);

        backPayment.AssignName("SBP-2019-00001");
        var entries = backPayment.SubmitWithEntries(NegativeArrearsPolicy.RecoverInPayroll);

        Assert.Equal(1_000m, entries.Single(e => e.Abbreviation == "BP_REC").Amount);
        Assert.Equal(250m, entries.Single(e => e.Abbreviation == "BP_TAX_REF").Amount);
        Assert.Equal(70m, entries.Single(e => e.Abbreviation == "BP_DED_REF").Amount);
        var effectOnNet = entries.Where(e => e.Type == ComponentType.Earning).Sum(e => e.Amount) - entries.Where(e => e.Type == ComponentType.Deduction).Sum(e => e.Amount);
        Assert.Equal(-680m, effectOnNet);
    }

    [Fact]
    public void A_downgrade_can_be_refused_by_settings()
    {
        var backPayment = Create(8_500m, PaidMonths(9_000m, (2026, 9)));
        backPayment.AssignName("SBP-X");

        Assert.Throws<Gebeta.SharedKernel.DomainException>(() => backPayment.SubmitWithEntries(NegativeArrearsPolicy.Refuse));
    }

    [Fact]
    public void An_earlier_back_payment_is_the_baseline_so_nothing_is_paid_twice()
    {
        var first = Create(13_000m, PaidMonths(12_000m, (2026, 8)));
        var baseline = BackPaymentCalculator.BaselineFromEarlier(first.Months[0], "SBP-1", 30, 30);

        var again = BackPaymentCalculator.Calculate(Standard, 13_000m, [baseline], EthiopianIncomeTax.ValidOn);
        var further = BackPaymentCalculator.Calculate(Standard, 14_000m, [baseline], EthiopianIncomeTax.ValidOn);

        Assert.Equal(0m, again[0].NetArrears);
        Assert.Equal(1_000m, further[0].GrossArrears);   // only the second increment
    }

    [Fact]
    public void Months_before_the_tax_change_use_their_own_tax_table()
    {
        // June 2025 falls under Proclamation 979/2016: 12,000 → 2,700 tax, 13,000 → 3,050 tax.
        var backPayment = Create(13_000m, PaidMonths(12_000m, (2025, 6)));

        Assert.Equal("ET-EMP-979", backPayment.Months[0].TaxTable);
        Assert.Equal(350m, backPayment.Months[0].AdditionalTax);
    }

    [Fact]
    public void The_arrears_must_be_paid_after_the_period_and_need_a_reason()
    {
        var months = BackPaymentCalculator.Calculate(Standard, 13_000m, PaidMonths(12_000m, (2026, 8)), EthiopianIncomeTax.ValidOn);

        Assert.Throws<Gebeta.SharedKernel.DomainException>(() => SalaryBackPayment.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "E", "T",
            Guid.CreateVersion7(), 13_000m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), "Reason", null, months));
        Assert.Throws<Gebeta.SharedKernel.DomainException>(() => SalaryBackPayment.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "E", "T",
            Guid.CreateVersion7(), 13_000m, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), new DateOnly(2026, 11, 25), " ", null, months));
    }
}
