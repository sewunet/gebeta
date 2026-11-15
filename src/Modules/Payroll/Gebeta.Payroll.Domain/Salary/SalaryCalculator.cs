using Gebeta.Payroll.Domain.Formulas;
using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

/// <param name="Base">Monthly base from the salary structure assignment.</param>
/// <param name="PaymentDays">Days paid this period (from HR attendance, or entered by hand).</param>
/// <param name="TotalWorkingDays">Days in the period; Ethiopian monthly payroll normally uses 30.</param>
/// <param name="PayrollDate">Chooses the income tax table.</param>
/// <param name="Additional">Extra amounts per component abbreviation (overtime, arrears, deductions).</param>
/// <param name="Variables">Employee variables the formulas use (e.g. grade, step).</param>
public sealed record SalaryInput(
    decimal Base,
    decimal PaymentDays,
    decimal TotalWorkingDays,
    DateOnly PayrollDate,
    IReadOnlyDictionary<string, decimal>? Additional = null,
    IReadOnlyDictionary<string, decimal>? Variables = null);

public sealed record SlipLine(string Component, string Abbreviation, ComponentType Type, decimal Amount, bool IsTaxable, string? Formula);

public sealed record SlipResult(
    IReadOnlyList<SlipLine> Lines,
    decimal GrossPay,
    decimal TaxableEarnings,
    decimal TotalDeductions,
    decimal NetPay,
    decimal EmployerContributions,
    string TaxTable);

/// <summary>
/// Calculates one salary slip from a structure (docs/modules/payroll.md). Pure: no database, no clock,
/// so the same input always gives the same slip, which back pay relies on.
/// </summary>
public static class SalaryCalculator
{
    public static SlipResult Calculate(SalaryStructure structure, SalaryInput input, IncomeTaxTable taxTable)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(taxTable);

        if (input.TotalWorkingDays <= 0m)
        {
            throw new DomainException("payroll.working_days", "Total working days must be more than zero.");
        }

        if (input.PaymentDays < 0m || input.PaymentDays > input.TotalWorkingDays)
        {
            throw new DomainException("payroll.payment_days", $"Payment days must be between 0 and {input.TotalWorkingDays}.");
        }

        if (input.Base < 0m)
        {
            throw new DomainException("payroll.base", "The base cannot be negative.");
        }

        var additional = input.Additional ?? new Dictionary<string, decimal>();
        var unknownAdditional = additional.Keys.Where(k => structure.Components.All(c => c.Abbreviation != k)).ToList();
        if (unknownAdditional.Count > 0)
        {
            throw new DomainException("payroll.additional", $"Additional amounts for components not in '{structure.Name}': {string.Join(", ", unknownAdditional)}.");
        }

        var scope = new Scope(taxTable);
        scope.Set("base", input.Base);
        scope.Set("payment_days", input.PaymentDays);
        scope.Set("total_working_days", input.TotalWorkingDays);
        foreach (var (name, value) in input.Variables ?? new Dictionary<string, decimal>())
        {
            scope.Set(name, value);
        }

        var lines = new List<SlipLine>();
        var totalsSet = false;
        foreach (var component in structure.Components)
        {
            if (!totalsSet && component.Line.Type != ComponentType.Earning)
            {
                // Earnings are done: deductions may now use the totals.
                scope.Set("gross_pay", lines.Sum(l => l.Amount));
                scope.Set("taxable_earnings", lines.Where(l => l.IsTaxable).Sum(l => l.Amount));
                totalsSet = true;
            }

            var amount = 0m;
            if (component.Condition is null || component.Condition.Evaluate(scope) != 0m)
            {
                amount = component.Formula?.Evaluate(scope) ?? component.Line.Amount!.Value;
                if (component.Line.DependsOnPaymentDays)
                {
                    amount = amount * input.PaymentDays / input.TotalWorkingDays;
                }
            }

            amount = Rounding.ToCurrency(amount + additional.GetValueOrDefault(component.Abbreviation));
            scope.Set(component.Abbreviation, amount);
            lines.Add(new SlipLine(component.Line.Name, component.Abbreviation, component.Line.Type, amount,
                component.Line.Type == ComponentType.Earning && component.Line.IsTaxable, component.Formula?.Text));
        }

        var gross = lines.Where(l => l.Type == ComponentType.Earning).Sum(l => l.Amount);
        var taxable = lines.Where(l => l.IsTaxable).Sum(l => l.Amount);
        var deductions = lines.Where(l => l.Type == ComponentType.Deduction).Sum(l => l.Amount);
        var employer = lines.Where(l => l.Type == ComponentType.EmployerContribution).Sum(l => l.Amount);
        return new SlipResult(lines, gross, taxable, deductions, gross - deductions, employer, taxTable.Code);
    }

    private sealed class Scope(IncomeTaxTable taxTable) : IFormulaScope
    {
        private readonly Dictionary<string, decimal> _values = new(StringComparer.Ordinal);

        public void Set(string name, decimal value) => _values[name] = value;

        public bool TryGetVariable(string name, out decimal value) => _values.TryGetValue(name, out value);

        public bool TryCall(string name, IReadOnlyList<decimal> arguments, out decimal result)
        {
            if (name == "income_tax" && arguments.Count == 1)
            {
                result = taxTable.Calculate(arguments[0]);
                return true;
            }

            result = 0m;
            return false;
        }
    }
}

/// <summary>A typical Ethiopian salary structure, used as the starting template for new companies.</summary>
public static class EthiopianSalaryStructures
{
    public static IReadOnlyList<SalaryComponentLine> Standard { get; } =
    [
        new("Basic Salary", "BS", ComponentType.Earning, Formula: "base", DependsOnPaymentDays: true),
        new("Income Tax", "IT", ComponentType.Deduction, Formula: "income_tax(taxable_earnings)"),
        new("Employee Pension (7%)", "PE", ComponentType.Deduction, Formula: "BS * 0.07"),
        new("Employer Pension (11%)", "PER", ComponentType.EmployerContribution, Formula: "BS * 0.11"),
    ];
}
