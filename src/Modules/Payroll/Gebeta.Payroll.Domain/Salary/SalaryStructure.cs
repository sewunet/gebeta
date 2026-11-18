using Gebeta.Payroll.Domain.Formulas;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

public enum ComponentType
{
    Earning,
    Deduction,

    /// <summary>Paid by the employer on top of salary (e.g. employer pension 11%); not part of net pay.</summary>
    EmployerContribution,
}

/// <summary>One component of a salary structure, e.g. Basic Salary (BS = base) or Income Tax (IT = income_tax(taxable_earnings)).</summary>
public sealed record SalaryComponentLine(
    string Name,
    string Abbreviation,
    ComponentType Type,
    string? Formula = null,
    decimal? Amount = null,
    string? Condition = null,
    bool IsTaxable = true,
    bool DependsOnPaymentDays = false);

/// <summary>
/// A validated salary structure: formulas parsed once, every name known, no cycles, components in
/// calculation order. Earnings come first; deductions and employer contributions may use the totals.
/// </summary>
public sealed class SalaryStructure
{
    /// <summary>Variables every formula may use.</summary>
    public static readonly IReadOnlySet<string> StandardVariables = new HashSet<string>(StringComparer.Ordinal)
    {
        "base", "payment_days", "total_working_days",
    };

    /// <summary>Totals available to deductions and employer contributions only.</summary>
    public static readonly IReadOnlySet<string> TotalVariables = new HashSet<string>(StringComparer.Ordinal)
    {
        "gross_pay", "taxable_earnings",
    };

    public static readonly IReadOnlySet<string> Functions = new HashSet<string>(StringComparer.Ordinal) { "income_tax" };

    private SalaryStructure(string name, IReadOnlyList<CompiledComponent> ordered)
    {
        Name = name;
        Components = ordered;
    }

    public string Name { get; }

    /// <summary>Components in calculation order.</summary>
    public IReadOnlyList<CompiledComponent> Components { get; }

    /// <param name="extraVariables">Employee variables the caller will supply (e.g. "grade", "step").</param>
    public static Result<SalaryStructure> Create(string name, IReadOnlyList<SalaryComponentLine> lines, IReadOnlyCollection<string>? extraVariables = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("A salary structure needs a name.");
        }

        if (lines.Count == 0)
        {
            return Error.Validation("A salary structure needs at least one component.");
        }

        var extras = extraVariables?.ToHashSet(StringComparer.Ordinal) ?? [];
        var compiled = new List<CompiledComponent>();
        foreach (var line in lines)
        {
            var result = Compile(line);
            if (result.IsFailure)
            {
                return result.Error!;
            }

            compiled.Add(result.Value);
        }

        var duplicates = compiled.GroupBy(c => c.Abbreviation, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
        {
            return Error.Validation($"Abbreviations must be unique: {string.Join(", ", duplicates)}.");
        }

        var abbreviations = compiled.Select(c => c.Abbreviation).ToHashSet(StringComparer.Ordinal);
        var earnings = compiled.Where(c => c.Line.Type == ComponentType.Earning).Select(c => c.Abbreviation).ToHashSet(StringComparer.Ordinal);
        foreach (var component in compiled)
        {
            var isEarning = component.Line.Type == ComponentType.Earning;
            foreach (var name2 in component.ReadsVariables)
            {
                var known = StandardVariables.Contains(name2) || extras.Contains(name2) || abbreviations.Contains(name2)
                    || (!isEarning && TotalVariables.Contains(name2));
                if (!known)
                {
                    var hint = isEarning && TotalVariables.Contains(name2) ? " (totals are only available to deductions)" : string.Empty;
                    return Error.Validation($"{component.Line.Name}: unknown name '{name2}'{hint}.");
                }

                if (isEarning && abbreviations.Contains(name2) && !earnings.Contains(name2))
                {
                    return Error.Validation($"{component.Line.Name}: an earning cannot depend on the deduction '{name2}'.");
                }
            }

            var unknownFunction = component.CallsFunctions.FirstOrDefault(f => !Functions.Contains(f));
            if (unknownFunction is not null)
            {
                return Error.Validation($"{component.Line.Name}: unknown function '{unknownFunction}'.");
            }
        }

        var ordered = Order(compiled.Where(c => c.Line.Type == ComponentType.Earning).ToList());
        if (ordered.IsFailure)
        {
            return ordered.Error!;
        }

        var rest = Order(compiled.Where(c => c.Line.Type != ComponentType.Earning).ToList());
        return rest.IsFailure ? rest.Error! : new SalaryStructure(name.Trim(), [.. ordered.Value, .. rest.Value]);
    }

    /// <summary>
    /// This structure, plus a zero component for each additional pay it does not have (overtime from an additional salary, a one-off
    /// deduction): the component then only carries the month's amount, taxed or not as the additional pay says. One rule for slips
    /// and for back payments recalculating a month.
    /// </summary>
    public SalaryStructure WithAdditional(IReadOnlyList<AdditionalPay> additional, IReadOnlyCollection<string>? variables)
    {
        ArgumentNullException.ThrowIfNull(additional);
        var missing = additional.Where(a => Components.All(c => c.Abbreviation != a.Abbreviation)).DistinctBy(a => a.Abbreviation).ToList();
        if (missing.Count == 0)
        {
            return this;
        }

        var lines = Components.Select(c => c.Line)
            .Concat(missing.Select(a => new SalaryComponentLine(a.Component, a.Abbreviation, a.Type, Amount: 0m, IsTaxable: a.IsTaxable)))
            .ToList();
        var extended = Create(Name, lines, variables);
        return extended.IsSuccess ? extended.Value : throw new DomainException("payroll.additional_component", extended.Error!.Message);
    }

    private static Result<CompiledComponent> Compile(SalaryComponentLine line)
    {
        if (string.IsNullOrWhiteSpace(line.Name))
        {
            return Error.Validation("Every component needs a name.");
        }

        var abbreviation = line.Abbreviation?.Trim() ?? string.Empty;
        if (abbreviation.Length is 0 or > 20 || !(char.IsAsciiLetter(abbreviation[0]) || abbreviation[0] == '_')
            || !abbreviation.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')
            || StandardVariables.Contains(abbreviation) || TotalVariables.Contains(abbreviation))
        {
            return Error.Validation($"{line.Name}: the abbreviation '{line.Abbreviation}' must be a short name of letters, digits and _ (e.g. BS, IT, PE_EMP).");
        }

        if ((line.Formula is null) == (line.Amount is null))
        {
            return Error.Validation($"{line.Name}: give either a formula or a fixed amount.");
        }

        Formula? formula = null;
        if (line.Formula is not null)
        {
            var parsed = Formula.Parse(line.Formula);
            if (parsed.IsFailure)
            {
                return Error.Validation($"{line.Name}: {parsed.Error!.Message}");
            }

            formula = parsed.Value;
        }

        Formula? condition = null;
        if (!string.IsNullOrWhiteSpace(line.Condition))
        {
            var parsed = Formula.Parse(line.Condition);
            if (parsed.IsFailure)
            {
                return Error.Validation($"{line.Name} condition: {parsed.Error!.Message}");
            }

            condition = parsed.Value;
        }

        return new CompiledComponent(line with { Abbreviation = abbreviation }, formula, condition);
    }

    /// <summary>Topological order by the abbreviations each component reads; reports cycles.</summary>
    private static Result<List<CompiledComponent>> Order(List<CompiledComponent> group)
    {
        var byAbbreviation = group.ToDictionary(c => c.Abbreviation, StringComparer.Ordinal);
        var ordered = new List<CompiledComponent>();
        var state = new Dictionary<string, bool>(StringComparer.Ordinal);
        string? cycle = null;

        void Visit(CompiledComponent component, List<string> path)
        {
            if (cycle is not null || state.TryGetValue(component.Abbreviation, out var done) && done)
            {
                return;
            }

            if (state.ContainsKey(component.Abbreviation))
            {
                cycle = string.Join(" → ", path.SkipWhile(p => p != component.Abbreviation).Append(component.Abbreviation));
                return;
            }

            state[component.Abbreviation] = false;
            path.Add(component.Abbreviation);
            foreach (var dependency in component.ReadsVariables.Where(byAbbreviation.ContainsKey).Order(StringComparer.Ordinal))
            {
                Visit(byAbbreviation[dependency], path);
            }

            path.RemoveAt(path.Count - 1);
            state[component.Abbreviation] = true;
            ordered.Add(component);
        }

        foreach (var component in group)
        {
            Visit(component, []);
        }

        return cycle is null ? ordered : Error.Validation($"Formulas depend on each other in a circle: {cycle}.");
    }
}

/// <summary>A component with its parsed formula and condition.</summary>
public sealed class CompiledComponent
{
    internal CompiledComponent(SalaryComponentLine line, Formula? formula, Formula? condition)
    {
        Line = line;
        Formula = formula;
        Condition = condition;
        ReadsVariables = (formula?.Variables ?? new HashSet<string>()).Concat(condition?.Variables ?? new HashSet<string>())
            .ToHashSet(StringComparer.Ordinal);
        CallsFunctions = (formula?.Functions ?? new HashSet<string>()).Concat(condition?.Functions ?? new HashSet<string>())
            .ToHashSet(StringComparer.Ordinal);
    }

    public SalaryComponentLine Line { get; }
    public string Abbreviation => Line.Abbreviation;
    public Formula? Formula { get; }
    public Formula? Condition { get; }
    public IReadOnlySet<string> ReadsVariables { get; }
    public IReadOnlySet<string> CallsFunctions { get; }
}
