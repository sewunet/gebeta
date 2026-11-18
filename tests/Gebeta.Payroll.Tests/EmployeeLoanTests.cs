using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Tests;

/// <summary>Employee loans and standing deductions (docs/requirements/competitive-features.md #5).</summary>
public class EmployeeLoanTests
{
    private static readonly DateOnly Sep1 = new(2026, 9, 1), Sep30 = new(2026, 9, 30), Oct1 = new(2026, 10, 1), Oct31 = new(2026, 10, 31),
        Nov1 = new(2026, 11, 1), Nov30 = new(2026, 11, 30), Dec1 = new(2026, 12, 1), Dec31 = new(2026, 12, 31);

    private static EmployeeLoan Loan(decimal principal = 2_500m, decimal instalment = 1_000m)
    {
        var loan = EmployeeLoan.Create(Guid.NewGuid(), Guid.NewGuid(), "E-1", "Almaz", LoanKind.Loan, "Salary advance", null, principal, instalment,
            new DateOnly(2026, 9, 15), null, null);
        loan.AssignName("LOAN-2019-00001");
        loan.Submit();
        return loan;
    }

    private static EmployeeLoan Idir(DateOnly? end = null)
    {
        var idir = EmployeeLoan.Create(Guid.NewGuid(), Guid.NewGuid(), "E-1", "Almaz", LoanKind.StandingDeduction, "Idir", "idir", null, 50m, Sep1, end, null);
        idir.AssignName("LOAN-2019-00002");
        idir.Submit();
        return idir;
    }

    [Fact]
    public void A_loan_is_deducted_from_its_start_period_and_the_last_instalment_is_only_what_is_left()
    {
        var loan = Loan();
        Assert.Equal(0m, loan.DueFor(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));
        Assert.Equal(1_000m, loan.TakeInstalment(Sep1, Sep30).Amount); // the period containing 15 September
        Assert.Equal(1_000m, loan.TakeInstalment(Oct1, Oct31).Amount);
        var last = loan.TakeInstalment(Nov1, Nov30);
        Assert.Equal((500m, "LOAN", "Salary advance"), (last.Amount, last.Abbreviation, last.Name));
        Assert.Equal((0m, true), (loan.Balance, loan.IsCompleted));
        Assert.Equal(0m, loan.DueFor(Dec1, Dec31));
    }

    [Fact]
    public void A_period_gets_its_instalment_once_even_when_the_slip_is_made_again()
    {
        var loan = Loan();
        loan.TakeInstalment(Sep1, Sep30);
        Assert.Equal(0m, loan.DueFor(Sep1, Sep30)); // the cancelled slip's entry waits for the corrected slip
        Assert.Throws<DomainException>(() => loan.TakeInstalment(Sep1, Sep30));
    }

    [Fact]
    public void A_paused_loan_skips_months_and_a_settled_loan_is_repaid_in_full()
    {
        var loan = Loan();
        loan.TakeInstalment(Sep1, Sep30);
        loan.Pause();
        Assert.Equal(0m, loan.DueFor(Oct1, Oct31));
        loan.Resume();
        Assert.Equal(1_000m, loan.DueFor(Nov1, Nov30));

        loan.Settle(new DateOnly(2026, 11, 5), "CR-2019-00012");
        Assert.Equal((0m, 2_500m, true), (loan.Balance, loan.Recovered, loan.IsCompleted));
        Assert.Equal(("Manual", 1_500m, "CR-2019-00012"), (loan.Repayments[^1].Source, loan.Repayments[^1].Amount, loan.Repayments[^1].Reference));
        Assert.Equal(0m, loan.DueFor(Nov1, Nov30));
        Assert.Throws<DomainException>(() => loan.Pause());
    }

    [Fact]
    public void A_standing_deduction_runs_until_its_end_or_until_it_is_stopped()
    {
        var idir = Idir(end: new DateOnly(2026, 10, 15));
        Assert.Equal(("IDIR", (decimal?)null), (idir.Abbreviation, idir.Balance));
        Assert.Equal(50m, idir.TakeInstalment(Sep1, Sep30).Amount);
        Assert.Equal(50m, idir.DueFor(Oct1, Oct31));
        Assert.Equal(0m, idir.DueFor(Nov1, Nov30));

        var open = Idir();
        open.Settle(new DateOnly(2026, 10, 20), null); // stopped after October's slip
        Assert.Equal((50m, 0m, true), (open.DueFor(Oct1, Oct31), open.DueFor(Nov1, Nov30), open.IsCompleted));
    }

    [Fact]
    public void Cancelling_is_refused_once_a_slip_took_an_instalment()
    {
        var loan = Loan();
        var entry = loan.TakeInstalment(Sep1, Sep30);
        entry.IncludeIn(Guid.NewGuid());
        Assert.Throws<DomainException>(() => loan.CancelWithEntries([entry]));
        entry.Release(); // the slip was cancelled
        loan.CancelWithEntries([entry]);
        Assert.Equal((DocStatus.Cancelled, true), (loan.DocStatus, entry.IsCancelled));
        Assert.Equal(0m, loan.DueFor(Oct1, Oct31));
    }

    [Theory]
    [InlineData("Loan", 0, 100, null)]            // no amount
    [InlineData("Loan", 100, 200, null)]          // instalment above the loan
    [InlineData("StandingDeduction", 100, 50, "IDIR")] // a total on a standing deduction
    [InlineData("StandingDeduction", null, 50, "LOAN")] // the loans' abbreviation
    [InlineData("StandingDeduction", null, 50, "")]
    public void Invalid_loans_are_refused(string kind, int? principal, int instalment, string? abbreviation) =>
        Assert.Throws<DomainException>(() => EmployeeLoan.Create(Guid.NewGuid(), Guid.NewGuid(), "E-1", "Almaz", Enum.Parse<LoanKind>(kind), "Deduction",
            abbreviation, principal, instalment, Sep1, null, null));
}
