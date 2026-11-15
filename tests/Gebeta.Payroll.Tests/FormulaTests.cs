using Gebeta.Payroll.Domain.Formulas;

namespace Gebeta.Payroll.Tests;

public class FormulaTests
{
    private sealed class Scope(Dictionary<string, decimal> values) : IFormulaScope
    {
        public bool TryGetVariable(string name, out decimal value) => values.TryGetValue(name, out value);

        public bool TryCall(string name, IReadOnlyList<decimal> arguments, out decimal result)
        {
            result = name == "double" ? arguments[0] * 2 : 0m;
            return name == "double";
        }
    }

    private static decimal Eval(string formula, params (string Name, decimal Value)[] values) =>
        Formula.Parse(formula).Value.Evaluate(new Scope(values.ToDictionary(v => v.Name, v => v.Value)));

    [Theory]
    [InlineData("1 + 2 * 3", "7")]
    [InlineData("(1 + 2) * 3", "9")]
    [InlineData("0.1 + 0.2", "0.3")]       // exact decimals, never 0.30000000000000004
    [InlineData("10 / 4", "2.5")]
    [InlineData("10 // 4", "2")]
    [InlineData("-7 % 3", "2")]            // Python modulo
    [InlineData("2 ** 10", "1024")]
    [InlineData("-2 ** 2", "-4")]          // Python: unary minus binds looser than **
    [InlineData("1_000 * 2", "2000")]
    public void Arithmetic_follows_python_precedence_with_exact_decimals(string formula, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Eval(formula));
    }

    [Theory]
    [InlineData("1 < 2 < 3", "1")]
    [InlineData("1 < 3 < 2", "0")]
    [InlineData("not 0", "1")]
    [InlineData("1 and 0 or 1", "1")]
    [InlineData("5 if 1 > 2 else 7", "7")]
    [InlineData("1 if 0 else 2 if 0 else 3", "3")]
    [InlineData("if(True, 10, 20)", "10")]
    [InlineData("max(1, 5, 3) + min(4, 2)", "7")]
    [InlineData("round(2.345, 2)", "2.35")]
    [InlineData("round(-2.5)", "-3")]       // half away from zero, not banker's rounding
    [InlineData("flt(1.23456, 2)", "1.23")]
    [InlineData("floor(2.7) + ceil(2.1) + abs(-1) + int(-3.9)", "3")]
    public void Logic_and_functions_work_like_erpnext_formulas(string formula, string expected)
    {
        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Eval(formula));
    }

    [Fact]
    public void Variables_and_scope_functions_are_used_and_reported()
    {
        var formula = Formula.Parse("double(BS) + base * 0.07").Value;

        Assert.Equal(["BS", "base"], formula.Variables.Order(StringComparer.Ordinal));
        Assert.Equal(["double"], formula.Functions);
        Assert.Equal(2070m, Eval("double(BS) + base * 0.07", ("BS", 1000m), ("base", 1000m)));
    }

    [Fact]
    public void Only_the_chosen_branch_is_evaluated()
    {
        // The unknown name in the other branch is never read, and division by zero never happens.
        Assert.Equal(1m, Eval("1 if x > 0 else 1 / 0", ("x", 5m)));
        Assert.Equal(2m, Eval("if(x > 0, 2, missing)", ("x", 5m)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("1 if 2")]
    [InlineData("__import__('os')")]       // quotes are not even valid characters
    [InlineData("doc.base")]               // no attribute access
    [InlineData("a = 1")]
    [InlineData("1 2")]
    public void Invalid_or_unsafe_formulas_are_refused(string formula)
    {
        Assert.True(Formula.Parse(formula).IsFailure);
    }

    [Fact]
    public void Runtime_errors_are_clear()
    {
        Assert.Contains("unknown name 'gross'", Assert.Throws<FormulaException>(() => Eval("gross * 2")).Message, StringComparison.Ordinal);
        Assert.Contains("Division by zero", Assert.Throws<FormulaException>(() => Eval("1 / x", ("x", 0m))).Message, StringComparison.Ordinal);
        Assert.Contains("unknown function 'sqrt'", Assert.Throws<FormulaException>(() => Eval("sqrt(4)")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Very_deep_nesting_is_refused()
    {
        Assert.True(Formula.Parse(new string('(', 100) + "1" + new string(')', 100)).IsFailure);
    }
}
