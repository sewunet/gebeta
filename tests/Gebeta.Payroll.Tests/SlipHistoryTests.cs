using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Tests;

/// <summary>Additional pay kept on slips, and slips paid before the cut-over kept as history.</summary>
public class SlipHistoryTests
{
    private static readonly SalaryComponentLine[] Lines =
    [
        .. EthiopianSalaryStructures.Standard,
        new("Overtime", "OT", ComponentType.Earning, Amount: 0m),
    ];

    private static SalaryStructureDefinition Structure() => SalaryStructureDefinition.Create(Guid.NewGuid(), "Staff", Lines).Value;

    private static SalaryStructureAssignment Assignment(SalaryStructureDefinition s, decimal baseAmount) =>
        SalaryStructureAssignment.Create(s.CompanyId, Guid.NewGuid(), s.Id, baseAmount, new DateOnly(2026, 7, 8)).Value;

    [Fact]
    public void A_back_payment_pays_the_months_overtime_again_and_taxes_the_whole()
    {
        var structure = Structure();
        var october = SalarySlip.Calculate(structure.CompanyId, Guid.NewGuid(), "EMP-1", "Test", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            structure, Assignment(structure, 10_000m), 30m, 30m, new Dictionary<string, decimal> { ["OT"] = 1_500m }, EthiopianIncomeTax.Current);
        Assert.Equal([new AdditionalPay("Overtime", "OT", ComponentType.Earning, true, 1_500m)], october.Additional);

        var month = BackPaymentCalculator.Calculate(structure.Compile(), 11_000m, [BackPaymentCalculator.BaselineFromSlip(october, structure.Compile())],
            EthiopianIncomeTax.ValidOn).Single();

        // Paid: 10,000 + 1,500 overtime, tax on 11,500 = 2,100. Now: 11,000 + 1,500, tax on 12,500 = 2,400.
        Assert.Equal((11_500m, 12_500m), (month.OldGross, month.NewGross));
        Assert.Equal(1_000m, month.GrossArrears); // without the overtime it would have been -500
        Assert.Equal(300m, month.AdditionalTax);
    }

    [Fact]
    public void A_history_slip_keeps_the_lines_as_paid_and_cannot_be_cancelled()
    {
        SalarySlipLine[] lines =
        [
            new("Basic Salary", "BS", ComponentType.Earning, 10_000m, null),
            new("Overtime", "OT", ComponentType.Earning, 1_500m, null),
            new("Income Tax", "IT", ComponentType.Deduction, 2_100m, null),
            new("Employee Pension", "PE", ComponentType.Deduction, 700m, null),
            new("Employer Pension", "PER", ComponentType.EmployerContribution, 1_100m, null),
        ];
        var slip = SalarySlip.Record(Guid.NewGuid(), Guid.NewGuid(), "EMP-1", "Test", new DateOnly(2026, 6, 8), new DateOnly(2026, 7, 7), Guid.Empty, "Staff 2018",
            10_000m, 30m, 30m, lines, 11_500m, "Sal Slip/HR-EMP-0001/00012", [new AdditionalPay("Overtime", "OT", ComponentType.Earning, true, 1_500m)]);

        Assert.True(slip.IsHistory);
        Assert.Equal(DocStatus.Submitted, slip.DocStatus);
        Assert.Equal("Sal Slip/HR-EMP-0001/00012", slip.Name);
        Assert.Equal((11_500m, 2_800m, 8_700m, 1_100m), (slip.GrossPay, slip.TotalDeductions, slip.NetPay, slip.EmployerContributions));
        Assert.Equal("ET-EMP-2025", slip.TaxTable);
        Assert.Contains("only kept as history", Assert.Throws<DomainException>(slip.Cancel).Message, StringComparison.Ordinal);
    }
}
