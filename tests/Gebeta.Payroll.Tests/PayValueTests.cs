using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;

namespace Gebeta.Payroll.Tests;

/// <summary>Pay values: amounts an assignment gives a structure's formulas besides the base (a contract amount, a fuel allowance).</summary>
public class PayValueTests
{
    private static readonly SalaryComponentLine[] Lines =
    [
        new("Basic Salary", "BS", ComponentType.Earning, Formula: "ctc", DependsOnPaymentDays: true),
        new("Fuel Allowance", "FA", ComponentType.Earning, Formula: "fuel_payment"),
        new("Income Tax", "IT", ComponentType.Deduction, Formula: "income_tax(taxable_earnings)"),
        new("Employee Pension", "PE", ComponentType.Deduction, Formula: "BS * 0.07"),
    ];

    private static SalaryStructureDefinition Structure() =>
        SalaryStructureDefinition.Create(Guid.NewGuid(), "Contract staff", Lines, ["ctc", "fuel_payment"]).Value;

    [Fact]
    public void A_structure_must_declare_the_pay_values_its_formulas_read()
    {
        var undeclared = SalaryStructureDefinition.Create(Guid.NewGuid(), "Contract staff", Lines, ["ctc"]);
        Assert.Contains("unknown name 'fuel_payment'", undeclared.Error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("BS", "has the name of a component")]
    [InlineData("base", "has the name of a component or of something Gebeta already provides")]
    [InlineData("fuel payment", "must be a name of letters, digits and _")]
    public void Pay_value_names_are_plain_names_that_clash_with_nothing(string name, string message)
    {
        var created = SalaryStructureDefinition.Create(Guid.NewGuid(), "Contract staff", Lines, ["ctc", "fuel_payment", name]);
        Assert.Contains(message, created.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_assignment_gives_every_pay_value_and_nothing_else()
    {
        var structure = Structure();
        Assert.Equal("Contract staff needs these pay values: fuel_payment.", structure.CheckValues(new Dictionary<string, decimal> { ["ctc"] = 1m }).Error!.Message);
        Assert.Equal("Contract staff does not use these pay values: bonus.",
            structure.CheckValues(new Dictionary<string, decimal> { ["ctc"] = 1m, ["fuel_payment"] = 1m, ["bonus"] = 1m }).Error!.Message);
        Assert.True(structure.CheckValues(new Dictionary<string, decimal> { ["ctc"] = 1m, ["fuel_payment"] = 1m }).IsSuccess);
    }

    [Fact]
    public void The_slip_uses_the_assignments_pay_values()
    {
        var structure = Structure();
        var assignment = SalaryStructureAssignment.Create(structure.CompanyId, Guid.NewGuid(), structure.Id, 0m, new DateOnly(2026, 7, 8),
            new Dictionary<string, decimal> { ["ctc"] = 10_000m, ["fuel_payment"] = 1_500m }).Value;

        var slip = SalarySlip.Calculate(structure.CompanyId, assignment.EmployeeId, "EMP-1", "Test", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31),
            structure, assignment, 15m, 30m, null, EthiopianIncomeTax.Current);

        // Half a month: BS 5,000 (depends on payment days) + FA 1,500 = 6,500; tax on 6,500 = 20% - 500 = 800; pension 350.
        Assert.Equal((5_000m, 1_500m, 800m, 350m), (slip.AmountOf("BS"), slip.AmountOf("FA"), slip.AmountOf("IT"), slip.AmountOf("PE")));
        Assert.Equal(5_350m, slip.NetPay);
    }
}
