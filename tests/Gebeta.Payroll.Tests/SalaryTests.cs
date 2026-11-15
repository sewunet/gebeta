using Gebeta.Payroll.Domain.Formulas;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;

namespace Gebeta.Payroll.Tests;

public class IncomeTaxTests
{
    /// <summary>The income tax formula used by the current ERPNext payroll (customized_forcommon demo data).</summary>
    private const string ErpNextTaxFormula =
        "0 if BS <= 2000 else BS*0.15 - 300 if BS <= 4000 else BS*0.20 - 500 if BS <= 7000 else " +
        "BS*0.25 - 850 if BS <= 10000 else BS*0.30 - 1350 if BS <= 14000 else BS*0.35 - 2050";

    private sealed class BasicScope(decimal bs) : IFormulaScope
    {
        public bool TryGetVariable(string name, out decimal value)
        {
            value = bs;
            return name == "BS";
        }

        public bool TryCall(string name, IReadOnlyList<decimal> arguments, out decimal result)
        {
            result = 0m;
            return false;
        }
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("2000", "0")]
    [InlineData("2000.01", "0")]          // 300.0015 - 300 = 0.0015 → 0.00
    [InlineData("2100", "15")]
    [InlineData("4000", "300")]
    [InlineData("7000", "900")]
    [InlineData("10000", "1650")]
    [InlineData("13000", "2550")]
    [InlineData("14000", "2850")]
    [InlineData("20000", "4950")]
    public void Current_table_matches_the_ethiopian_slabs(string taxable, string expected)
    {
        var amount = decimal.Parse(taxable, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), EthiopianIncomeTax.Current.Calculate(amount));
    }

    [Fact]
    public void The_erpnext_formula_runs_unchanged_and_agrees_with_the_tax_table_everywhere()
    {
        var formula = Formula.Parse(ErpNextTaxFormula).Value;
        for (var bs = 0m; bs <= 20_000m; bs += 0.50m)
        {
            var fromFormula = Math.Round(formula.Evaluate(new BasicScope(bs)), 2, MidpointRounding.AwayFromZero);
            Assert.Equal(fromFormula, EthiopianIncomeTax.Current.Calculate(bs));
        }
    }

    [Theory]
    [InlineData("600", "0")]
    [InlineData("1650", "105")]
    [InlineData("5000", "697.5")]
    [InlineData("13000", "3050")]
    public void Proclamation_979_table_is_kept_for_earlier_periods(string taxable, string expected)
    {
        var amount = decimal.Parse(taxable, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), EthiopianIncomeTax.Proclamation979.Calculate(amount));
    }

    [Fact]
    public void The_table_is_chosen_by_payroll_date()
    {
        Assert.Same(EthiopianIncomeTax.Proclamation979, EthiopianIncomeTax.ValidOn(new DateOnly(2025, 6, 30)));
        Assert.Same(EthiopianIncomeTax.Current, EthiopianIncomeTax.ValidOn(new DateOnly(2026, 11, 30)));
    }

    [Fact]
    public void Tables_with_gaps_or_open_middle_slabs_are_refused()
    {
        Assert.True(IncomeTaxTable.Create("X", "X", new DateOnly(2026, 1, 1), null, "test",
            [new TaxSlab(0m, 1000m, 0m, 0m), new TaxSlab(1500m, null, 10m, 0m)]).IsFailure);
        Assert.True(IncomeTaxTable.Create("X", "X", new DateOnly(2026, 1, 1), null, "test",
            [new TaxSlab(0m, null, 0m, 0m), new TaxSlab(1000m, null, 10m, 0m)]).IsFailure);
    }
}

public class SalaryStructureTests
{
    [Fact]
    public void Components_are_ordered_by_their_dependencies()
    {
        var structure = SalaryStructure.Create("S",
        [
            new("Housing", "HA", ComponentType.Earning, Formula: "BS * 0.2"),
            new("Basic", "BS", ComponentType.Earning, Formula: "base"),
            new("Pension", "PE", ComponentType.Deduction, Formula: "BS * 0.07"),
        ]).Value;

        Assert.Equal(["BS", "HA", "PE"], structure.Components.Select(c => c.Abbreviation));
    }

    [Theory]
    [InlineData("A", "B", "Formulas depend on each other in a circle")]
    [InlineData("A", "missing", "unknown name 'missing'")]
    [InlineData("A", "gross_pay", "totals are only available to deductions")]
    [InlineData("A", "IT", "cannot depend on the deduction")]
    public void Invalid_structures_are_explained(string abbreviation, string reads, string message)
    {
        var result = SalaryStructure.Create("S",
        [
            new("First", abbreviation, ComponentType.Earning, Formula: $"{reads} + 1"),
            new("Second", "B", ComponentType.Earning, Formula: "A + 1"),
            new("Tax", "IT", ComponentType.Deduction, Formula: "income_tax(taxable_earnings)"),
        ]);

        Assert.True(result.IsFailure);
        Assert.Contains(message, result.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_abbreviations_and_bad_names_are_refused()
    {
        Assert.Contains("unique", SalaryStructure.Create("S",
            [new("A", "BS", ComponentType.Earning, Amount: 1m), new("B", "BS", ComponentType.Earning, Amount: 2m)]).Error!.Message, StringComparison.Ordinal);
        Assert.True(SalaryStructure.Create("S", [new("Pension 7%", "P7%", ComponentType.Deduction, Amount: 1m)]).IsFailure);
        Assert.True(SalaryStructure.Create("S", [new("No formula", "NF", ComponentType.Earning)]).IsFailure);
    }
}

public class SalaryCalculatorTests
{
    private static readonly DateOnly November = new(2026, 11, 30);

    private static SlipResult Standard(decimal basic, decimal paymentDays = 30, IReadOnlyDictionary<string, decimal>? additional = null, IReadOnlyList<SalaryComponentLine>? extra = null)
    {
        var lines = EthiopianSalaryStructures.Standard.Concat(extra ?? []).ToList();
        var structure = SalaryStructure.Create("Standard", lines).Value;
        return SalaryCalculator.Calculate(structure, new SalaryInput(basic, paymentDays, 30, November, additional), EthiopianIncomeTax.ValidOn(November));
    }

    // The four demo employees of the current Salary Back Payment data, at their new salaries.
    [Theory]
    [InlineData("13000", "2550", "910", "9540", "1430")]
    [InlineData("14500", "3025", "1015", "10460", "1595")]
    [InlineData("2200", "30", "154", "2016", "242")]
    [InlineData("8500", "1275", "595", "6630", "935")]
    public void Standard_ethiopian_slip(string basic, string tax, string pension, string net, string employerPension)
    {
        static decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        var slip = Standard(D(basic));

        Assert.Equal(D(tax), slip.Lines.Single(l => l.Abbreviation == "IT").Amount);
        Assert.Equal(D(pension), slip.Lines.Single(l => l.Abbreviation == "PE").Amount);
        Assert.Equal(D(net), slip.NetPay);
        Assert.Equal(D(employerPension), slip.EmployerContributions);
        Assert.Equal(slip.GrossPay - slip.TotalDeductions, slip.NetPay);
    }

    [Fact]
    public void Basic_salary_follows_payment_days()
    {
        var slip = Standard(12_000m, paymentDays: 15);

        Assert.Equal(6_000m, slip.GrossPay);
        Assert.Equal(6_000m * 0.20m - 500m, slip.Lines.Single(l => l.Abbreviation == "IT").Amount);
    }

    [Fact]
    public void Non_taxable_allowances_do_not_raise_income_tax_and_additional_amounts_do()
    {
        var transport = new SalaryComponentLine("Transport Allowance", "TA", ComponentType.Earning, Amount: 1_000m, IsTaxable: false);
        var overtime = new SalaryComponentLine("Overtime", "OT", ComponentType.Earning, Amount: 0m);

        var slip = Standard(13_000m, additional: new Dictionary<string, decimal> { ["OT"] = 1_000m }, extra: [transport, overtime]);

        Assert.Equal(15_000m, slip.GrossPay);
        Assert.Equal(14_000m, slip.TaxableEarnings);
        Assert.Equal(2_850m, slip.Lines.Single(l => l.Abbreviation == "IT").Amount);
    }

    [Fact]
    public void Conditions_switch_components_on_and_off()
    {
        var structure = SalaryStructure.Create("S",
        [
            new("Basic", "BS", ComponentType.Earning, Formula: "base"),
            new("Hardship Allowance", "HA", ComponentType.Earning, Formula: "BS * 0.25", Condition: "remote == 1"),
        ], ["remote"]).Value;

        var remote = SalaryCalculator.Calculate(structure, new SalaryInput(8_000m, 30, 30, November, Variables: new Dictionary<string, decimal> { ["remote"] = 1 }), EthiopianIncomeTax.Current);
        var office = SalaryCalculator.Calculate(structure, new SalaryInput(8_000m, 30, 30, November, Variables: new Dictionary<string, decimal> { ["remote"] = 0 }), EthiopianIncomeTax.Current);

        Assert.Equal(10_000m, remote.GrossPay);
        Assert.Equal(8_000m, office.GrossPay);
    }

    [Fact]
    public void Back_pay_for_an_earlier_period_uses_the_earlier_tax_table()
    {
        var structure = SalaryStructure.Create("Standard", EthiopianSalaryStructures.Standard).Value;
        var june2025 = new DateOnly(2025, 6, 30);

        var slip = SalaryCalculator.Calculate(structure, new SalaryInput(13_000m, 30, 30, june2025), EthiopianIncomeTax.ValidOn(june2025));

        Assert.Equal(3_050m, slip.Lines.Single(l => l.Abbreviation == "IT").Amount);
        Assert.Equal("ET-EMP-979", slip.TaxTable);
    }

    [Fact]
    public void Invalid_input_is_refused()
    {
        var structure = SalaryStructure.Create("Standard", EthiopianSalaryStructures.Standard).Value;

        Assert.ThrowsAny<Exception>(() => SalaryCalculator.Calculate(structure, new SalaryInput(1000m, 31, 30, November), EthiopianIncomeTax.Current));
        Assert.ThrowsAny<Exception>(() => SalaryCalculator.Calculate(structure, new SalaryInput(1000m, 30, 30, November, new Dictionary<string, decimal> { ["XX"] = 5 }), EthiopianIncomeTax.Current));
    }
}
