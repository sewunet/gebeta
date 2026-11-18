using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.BackPay;

/// <summary>The figures of one month before the revision: from its submitted slip, or from an earlier back payment.</summary>
public sealed record MonthBaseline(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Source,
    decimal PaymentDays,
    decimal TotalWorkingDays,
    decimal Gross,
    decimal Taxable,
    decimal Tax,
    decimal OtherDeductions,
    decimal EmployerContributions,
    IReadOnlyList<AdditionalPay>? Additional = null);

/// <summary>One month of a back payment: old and new figures and the differences.</summary>
public sealed record BackPaymentMonth(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Source,
    string TaxTable,
    decimal OldGross,
    decimal NewGross,
    decimal OldTaxable,
    decimal NewTaxable,
    decimal OldTax,
    decimal NewTax,
    decimal OldOtherDeductions,
    decimal NewOtherDeductions,
    decimal OldEmployerContributions,
    decimal NewEmployerContributions)
{
    public decimal GrossArrears => NewGross - OldGross;

    /// <summary>New tax of the month minus the tax already withheld for it (the arrears tax stays in its own month).</summary>
    public decimal AdditionalTax => NewTax - OldTax;

    /// <summary>Employee pension and other salary-based deductions on the difference.</summary>
    public decimal OtherDeductionsArrears => NewOtherDeductions - OldOtherDeductions;

    public decimal EmployerContributionsArrears => NewEmployerContributions - OldEmployerContributions;

    public decimal NetArrears => GrossArrears - AdditionalTax - OtherDeductionsArrears;
}

/// <summary>
/// Recalculates past months at a revised base salary (docs/modules/payroll.md, Salary back payment):
/// each month is recalculated with the same structure, payment days and the tax table valid in that month,
/// and compared with what was already paid. Pure domain logic: no database, no clock.
/// </summary>
public static class BackPaymentCalculator
{
    /// <param name="values">The revised assignment's pay values (a raise may change those instead of, or with, the base).</param>
    public static IReadOnlyList<BackPaymentMonth> Calculate(SalaryStructure structure, decimal newBase, IReadOnlyList<MonthBaseline> months,
        Func<DateOnly, IncomeTaxTable> taxTableFor, IReadOnlyDictionary<string, decimal>? values = null)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(months);
        ArgumentNullException.ThrowIfNull(taxTableFor);
        if (months.Count == 0)
        {
            throw new DomainException("payroll.back_payment_no_months", "There are no paid months in the period to recalculate.");
        }

        var taxComponents = TaxComponents(structure);
        var result = new List<BackPaymentMonth>(months.Count);
        foreach (var month in months.OrderBy(m => m.PeriodStart))
        {
            var table = taxTableFor(month.PeriodEnd);
            var paidOnTop = month.Additional ?? [];
            var slip = SalaryCalculator.Calculate(structure.WithAdditional(paidOnTop, values?.Keys.ToList()),
                new SalaryInput(newBase, month.PaymentDays, month.TotalWorkingDays, month.PeriodEnd,
                    paidOnTop.GroupBy(a => a.Abbreviation).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount)), values), table);
            var (tax, other) = SplitDeductions(slip.Lines.Select(l => (l.Abbreviation, l.Type, l.Amount)), taxComponents);

            result.Add(new BackPaymentMonth(month.PeriodStart, month.PeriodEnd, month.Source, table.Code,
                month.Gross, slip.GrossPay, month.Taxable, slip.TaxableEarnings, month.Tax, tax,
                month.OtherDeductions, other, month.EmployerContributions, slip.EmployerContributions));
        }

        return result;
    }

    /// <summary>The figures of a submitted slip, using only the structure's own lines (not earlier arrears lines).</summary>
    public static MonthBaseline BaselineFromSlip(SalarySlip slip, SalaryStructure structure)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(structure);
        var own = slip.Lines.Where(l => !l.IsAdditional).ToList();
        var taxComponents = TaxComponents(structure);
        var (tax, other) = SplitDeductions(own.Select(l => (l.Abbreviation, l.Type, l.Amount)), taxComponents);
        var taxable = own.Where(l => l.Type == ComponentType.Earning && IsTaxable(structure, l.Abbreviation)).Sum(l => l.Amount);
        var outside = slip.Additional.Where(a => structure.Components.All(c => c.Abbreviation != a.Abbreviation));
        taxable += outside.Where(a => a.Type == ComponentType.Earning && a.IsTaxable).Sum(a => a.Amount); // e.g. overtime from the previous system
        return new MonthBaseline(slip.PeriodStart, slip.PeriodEnd, slip.Name ?? slip.Id.ToString(), slip.PaymentDays, slip.TotalWorkingDays,
            own.Where(l => l.Type == ComponentType.Earning).Sum(l => l.Amount), taxable, tax, other,
            own.Where(l => l.Type == ComponentType.EmployerContribution).Sum(l => l.Amount), slip.Additional);
    }

    /// <summary>After an earlier back payment, its "new" figures are what was really paid for the month.</summary>
    /// <param name="additional">The month's own additional pay (from its slip): it is part of the month whichever back payment recalculates it.</param>
    public static MonthBaseline BaselineFromEarlier(BackPaymentMonth earlier, string backPaymentNumber, decimal paymentDays, decimal totalWorkingDays,
        IReadOnlyList<AdditionalPay>? additional = null)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        return new MonthBaseline(earlier.PeriodStart, earlier.PeriodEnd, backPaymentNumber, paymentDays, totalWorkingDays,
            earlier.NewGross, earlier.NewTaxable, earlier.NewTax, earlier.NewOtherDeductions, earlier.NewEmployerContributions, additional);
    }

    private static HashSet<string> TaxComponents(SalaryStructure structure) =>
        structure.Components.Where(c => c.CallsFunctions.Contains("income_tax")).Select(c => c.Abbreviation).ToHashSet(StringComparer.Ordinal);

    private static bool IsTaxable(SalaryStructure structure, string abbreviation) =>
        structure.Components.FirstOrDefault(c => c.Abbreviation == abbreviation)?.Line.IsTaxable ?? true;

    private static (decimal Tax, decimal Other) SplitDeductions(IEnumerable<(string Abbreviation, ComponentType Type, decimal Amount)> lines, HashSet<string> taxComponents)
    {
        var deductions = lines.Where(l => l.Type == ComponentType.Deduction).ToList();
        return (deductions.Where(l => taxComponents.Contains(l.Abbreviation)).Sum(l => l.Amount),
                deductions.Where(l => !taxComponents.Contains(l.Abbreviation)).Sum(l => l.Amount));
    }
}
