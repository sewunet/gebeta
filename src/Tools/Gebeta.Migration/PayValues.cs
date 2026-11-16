using System.Globalization;
using System.Text.RegularExpressions;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Formulas;
using Gebeta.Payroll.Domain.Salary;

namespace Gebeta.Migration;

/// <summary>
/// An ERPNext salary structure as Gebeta will hold it. <see cref="Variables"/> are the pay values its formulas read from ERPNext
/// records (the employee's <c>ctc</c>, an assignment field); <see cref="Sources"/> says which record each comes from, so every
/// assignment can be given its amounts. <see cref="Renamed"/>: abbreviations Gebeta cannot read, and what they became.
/// </summary>
public sealed record MappedStructure(
    string Name,
    IReadOnlyList<SalaryComponentDto> Components,
    IReadOnlyList<string> Variables,
    IReadOnlyDictionary<string, string> Sources,
    IReadOnlyDictionary<string, string> Renamed,
    IReadOnlyList<string> Problems,
    IReadOnlyList<string> Warnings);

public static partial class PayValues
{
    public const string FromAssignment = "Salary Structure Assignment";
    public const string FromEmployee = "Employee";
    public const string FromSlip = "Salary Slip";

    private const string IncomeTaxFormula = "income_tax(taxable_earnings)";

    /// <summary>
    /// ERPNext earnings and deductions as Gebeta components. Names a formula reads are resolved the way ERPNext resolves them: the
    /// structure's own components first; then any other salary component's abbreviation, which ERPNext counts as 0 (written as 0
    /// here, with a warning); then a field of the assignment, the employee or the slip (a pay value of the structure). Anything else
    /// stays for Gebeta's validation to report.
    /// </summary>
    public static MappedStructure Map(ErpSalaryStructure structure, ErpFormulaScope scope)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(scope);
        var problems = new List<string>();
        var warnings = new List<string>();
        var taken = structure.Rows.Select(r => r.Abbreviation).Where(Codes.IsValidAbbreviation).ToHashSet(StringComparer.Ordinal);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
        var own = structure.Rows.Select(r => r.Abbreviation).ToHashSet(StringComparer.Ordinal);
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var components = new List<SalaryComponentDto>();

        foreach (var d in structure.Rows)
        {
            var label = $"Salary structure \"{structure.Name}\", {d.Component} ({d.Abbreviation})";
            ComponentType type;
            if (!d.Statistical && !d.NotInTotal)
            {
                type = d.IsDeduction ? ComponentType.Deduction : ComponentType.Earning;
            }
            else if (d.IsDeduction)
            {
                // A deduction kept out of the totals is how ERPNext setups usually carry the employer's pension (11%).
                type = ComponentType.EmployerContribution;
                warnings.Add($"{label}: kept out of the totals in ERPNext, so it becomes an employer contribution (posted as the employer's cost). Check that is what it is.");
            }
            else
            {
                problems.Add($"{label}: a statistical earning (worked out, never paid) has no Gebeta equivalent yet; rewrite the formulas that use it.");
                continue;
            }

            var abbreviation = d.Abbreviation;
            if (!Codes.IsValidAbbreviation(abbreviation))
            {
                abbreviation = renamed.TryGetValue(d.Abbreviation, out var done) ? done : Codes.Abbreviation(d.Abbreviation, taken);
                renamed[d.Abbreviation] = abbreviation;
                warnings.Add($"{label}: abbreviation becomes {abbreviation} (Gebeta abbreviations are letters, digits and _; no formula can name the old one).");
            }

            var formula = d.TaxSlabBased ? IncomeTaxFormula : Resolve(d.Formula?.Trim(), label, own, scope, sources, warnings);
            var condition = Resolve(d.Condition?.Trim(), label, own, scope, sources, warnings);
            components.Add(new SalaryComponentDto(d.Component, abbreviation, type.ToString(), formula, formula is null ? d.Amount : null, condition,
                d.IsTaxable, d.DependsOnPaymentDays));
        }

        foreach (var (name, from) in sources.Where(s => s.Value != FromAssignment).OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            warnings.Add(from == FromEmployee
                ? $"Salary structure \"{structure.Name}\": '{name}' is read from the employee record. ERPNext keeps no history of it, so every assignment gets the employee's current value."
                : $"Salary structure \"{structure.Name}\": '{name}' is a salary slip field; it becomes a pay value of 0. Check the formulas that use it.");
        }

        return new MappedStructure(structure.Name, components, sources.Keys.Order(StringComparer.Ordinal).ToList(), sources, renamed, problems, warnings);
    }

    private static string? Resolve(string? text, string label, HashSet<string> own, ErpFormulaScope scope, Dictionary<string, string> sources, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(text) || Formula.Parse(text) is not { IsSuccess: true } parsed)
        {
            return string.IsNullOrWhiteSpace(text) ? null : text; // unreadable text: Gebeta's validation names the problem
        }

        foreach (var name in parsed.Value.Variables.Where(n => !own.Contains(n) && !SalaryStructure.StandardVariables.Contains(n) && !SalaryStructure.TotalVariables.Contains(n)))
        {
            if (scope.ComponentAbbreviations.Contains(name))
            {
                text = Regex.Replace(text, $@"(?<![A-Za-z0-9_.]){Regex.Escape(name)}(?![A-Za-z0-9_])", "0");
                warnings.Add($"{label}: '{name}' is a salary component outside this structure, which ERPNext counts as 0; the formula now says 0.");
            }
            else if (scope.AssignmentFields.Contains(name))
            {
                sources.TryAdd(name, FromAssignment);
            }
            else if (scope.EmployeeFields.Contains(name))
            {
                sources.TryAdd(name, FromEmployee);
            }
            else if (scope.SlipFields.Contains(name))
            {
                sources.TryAdd(name, FromSlip);
            }
        }

        return text;
    }

    /// <summary>One assignment's amounts for its structure's pay values, from the ERPNext records the formulas read them from.</summary>
    public static (Dictionary<string, decimal> Values, List<string> Problems) For(ErpAssignment assignment, MappedStructure structure,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> assignmentFields, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> employeeFields)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(structure);
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var name in structure.Variables)
        {
            var text = structure.Sources[name] switch
            {
                FromAssignment => assignment.Name is { } id ? assignmentFields.GetValueOrDefault(id)?.GetValueOrDefault(name) : null,
                FromEmployee => employeeFields.GetValueOrDefault(assignment.Employee)?.GetValueOrDefault(name),
                _ => null,
            };
            if (text is null)
            {
                values[name] = 0m; // ERPNext reads an empty field as 0 in arithmetic
            }
            else if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
            {
                values[name] = amount;
            }
            else
            {
                problems.Add($"{assignment.Employee} from {assignment.FromDate:yyyy-MM-dd}: '{name}' is \"{text}\", not an amount; rewrite the formulas of \"{structure.Name}\" that use it.");
            }
        }

        return (values, problems);
    }
}
