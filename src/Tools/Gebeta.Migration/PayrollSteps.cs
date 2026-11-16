using System.Globalization;
using Gebeta.Core.Application.Employees;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Employees;
using Gebeta.Payroll.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Migration;

/// <summary>
/// Employees, salary structures and their assignments (docs/data-migration.md steps 3–4). Formulas and conditions are copied
/// unchanged: Gebeta reads the same syntax, and anything it cannot read is listed for rewriting. The payroll step is the proof:
/// it recalculates the latest submitted payroll in Gebeta and compares every slip with ERPNext's, to the cent. It writes nothing.
/// </summary>
public sealed partial class MigrationRun
{
    private const string IncomeTaxFormula = "income_tax(taxable_earnings)";

    /// <summary>Everyone still employed (status other than Left). The father's name falls back to the second word of the full name.</summary>
    private async Task<StepReport> EmployeesAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("employees");
        var all = await source.EmployeesAsync(options.Company, cancellationToken).ConfigureAwait(false);
        var employees = all.Where(e => e.Status != "Left").ToList();
        if (all.Count > employees.Count)
        {
            report.Warnings.Add($"{all.Count - employees.Count} employees who left are not migrated (their slip history comes with step 5).");
        }

        var known = await KnownAsync("Employee", cancellationToken).ConfigureAwait(false);
        foreach (var e in employees)
        {
            if (known.ContainsKey(e.Name))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var words = e.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var father = e.FatherName ?? (words.Length > 1 ? words[1] : null);
            var grandfather = e.GrandfatherName ?? (e.FatherName is null && words.Length > 2 ? words[2] : null);
            if (father is null)
            {
                report.Failures.Add($"{e.Name} ({e.FullName}): no father's name; fill in the middle name in ERPNext.");
                continue;
            }

            if (!Enum.TryParse<Gender>(e.Gender, ignoreCase: true, out var gender))
            {
                report.Failures.Add($"{e.Name} ({e.FullName}): gender \"{e.Gender}\" is neither Male nor Female.");
                continue;
            }

            var tin = e.Tin;
            if (tin is not null && TaxpayerIdentificationNumber.Create(tin).IsFailure)
            {
                report.Warnings.Add($"{e.Name} ({e.FullName}): TIN \"{tin}\" is not 10 digits; imported without a TIN.");
                tin = null;
            }

            var check = Employee.Create(Guid.NewGuid(), "CHECK", e.FirstName, father, grandfather, gender, e.DateOfJoining, tin);
            if (check.IsFailure)
            {
                report.Failures.Add($"{e.Name} ({e.FullName}): {check.Error!.Message}");
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<CreateEmployeeCommand, EmployeeDto>(
                new CreateEmployeeCommand(_companyId, e.FirstName, father, grandfather, gender, e.DateOfJoining, tin), cancellationToken).ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"{e.Name} ({e.FullName}): {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Employee", e.Name, created.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
            if (e.RelievingDate is { } leaving) // still employed, leaving date already known
            {
                var left = await Handle<SetEmployeeLeavingCommand, EmployeeDto>(new SetEmployeeLeavingCommand(created.Value.Id, leaving), cancellationToken).ConfigureAwait(false);
                if (left.IsFailure)
                {
                    report.Warnings.Add($"{e.Name}: leaving date {leaving:yyyy-MM-dd}: {left.Error!.Message}");
                }
            }
        }

        await CheckAsync(report, "Employee", employees.Count, cancellationToken).ConfigureAwait(false);
        return report;
    }

    private async Task<StepReport> SalaryStructuresAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("structures");
        var structures = await source.SalaryStructuresAsync(options.Company, cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync("Salary Structure", cancellationToken).ConfigureAwait(false);
        foreach (var s in structures)
        {
            if (known.ContainsKey(s.Name))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var mapped = MapStructure(s);
            report.Warnings.AddRange(mapped.Warnings);
            if (mapped.Problems.Count > 0)
            {
                report.Failures.AddRange(mapped.Problems);
                continue;
            }

            if (options.DryRun)
            {
                var lines = mapped.Components.Select(c => new SalaryComponentLine(c.Name, c.Abbreviation, Enum.Parse<ComponentType>(c.Type), c.Formula, c.Amount, c.Condition,
                    c.IsTaxable, c.DependsOnPaymentDays)).ToList();
                var check = SalaryStructure.Create(s.Name, lines);
                if (check.IsFailure)
                {
                    report.Failures.Add($"Salary structure \"{s.Name}\": {check.Error!.Message}");
                    continue;
                }

                report.Created++;
                continue;
            }

            var created = await Handle<CreateSalaryStructureCommand, SalaryStructureDto>(new CreateSalaryStructureCommand(_companyId, s.Name, mapped.Components), cancellationToken)
                .ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"Salary structure \"{s.Name}\": {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Salary Structure", s.Name, created.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        await CheckAsync(report, "Salary Structure", structures.Count, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>ERPNext earnings and deductions as Gebeta components; problems are what Gebeta has no equivalent for.</summary>
    public static (List<SalaryComponentDto> Components, List<string> Problems, List<string> Warnings, Dictionary<string, string> Renamed) MapStructure(ErpSalaryStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var components = new List<SalaryComponentDto>();
        var problems = new List<string>();
        var warnings = new List<string>();
        var taken = structure.Rows.Select(r => r.Abbreviation).Where(Codes.IsValidAbbreviation).ToHashSet(StringComparer.Ordinal);
        var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
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

            var formula = d.TaxSlabBased ? IncomeTaxFormula : d.Formula?.Trim();
            components.Add(new SalaryComponentDto(d.Component, abbreviation, type.ToString(), formula, formula is null ? d.Amount : null, d.Condition?.Trim(),
                d.IsTaxable, d.DependsOnPaymentDays));
        }

        return (components, problems, warnings, renamed);
    }

    /// <summary>The whole assignment history, oldest first, so back pay across the cut-over has the earlier bases.</summary>
    private async Task<StepReport> AssignmentsAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("assignments");
        var rows = await source.AssignmentsAsync(options.Company, cancellationToken).ConfigureAwait(false);
        var employee = await ResolverAsync("Employee", cancellationToken).ConfigureAwait(false);
        var structure = await ResolverAsync("Salary Structure", cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync("Salary Structure Assignment", cancellationToken).ConfigureAwait(false);
        var expected = 0;
        var forLeavers = 0;
        foreach (var a in rows)
        {
            if (employee(a.Employee) is not { } employeeId)
            {
                forLeavers++; // employees who left are not migrated
                continue;
            }

            expected++;
            var key = $"{a.Employee}/{a.FromDate:yyyy-MM-dd}";
            if (known.ContainsKey(key))
            {
                report.AlreadyMigrated++;
                continue;
            }

            if (structure(a.Structure) is not { } structureId)
            {
                report.Failures.Add($"{a.Employee} from {a.FromDate:yyyy-MM-dd}: salary structure \"{a.Structure}\" is not migrated.");
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<AssignSalaryStructureCommand, SalaryStructureAssignmentDto>(new AssignSalaryStructureCommand(employeeId, structureId, a.Base, a.FromDate), cancellationToken)
                .ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"{a.Employee} from {a.FromDate:yyyy-MM-dd}: {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Salary Structure Assignment", key, created.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        if (forLeavers > 0)
        {
            report.Warnings.Add($"{forLeavers} assignments of employees who left are not migrated.");
        }

        var done = report.Created + report.AlreadyMigrated;
        report.CheckPassed = done == expected;
        report.Check = $"{(options.DryRun ? "dry run: " : "")}{done} of {expected} assignments";
        return report;
    }

    /// <summary>
    /// The proof (step 4): every slip of ERPNext's latest submitted payroll, recalculated by Gebeta's engine from the migrated
    /// structure with the same base, payment days and additional salary, must match ERPNext component by component and in net pay.
    /// </summary>
    private async Task<StepReport> PayrollCheckAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("payroll");
        var slips = await source.LatestSlipsAsync(options.Company, options.Cutover, cancellationToken).ConfigureAwait(false);
        if (slips.Count == 0)
        {
            report.Check = "no submitted salary slips to compare";
            return report;
        }

        var stored = await StructuresForCheckAsync(cancellationToken).ConfigureAwait(false);
        var renamed = (await source.SalaryStructuresAsync(options.Company, cancellationToken).ConfigureAwait(false))
            .ToDictionary(s => s.Name, s => (IReadOnlyDictionary<string, string>)MapStructure(s).Renamed, StringComparer.Ordinal);
        var bases = (await source.AssignmentsAsync(options.Company, cancellationToken).ConfigureAwait(false)).ToLookup(a => a.Employee, StringComparer.Ordinal);
        var equal = 0;
        var differences = new List<string>();
        foreach (var slip in slips)
        {
            // The base the slip was made with: an assignment entered later (a backdated raise) is back pay, not part of this slip.
            var valid = bases[slip.Employee].Where(a => a.FromDate <= slip.StartDate).ToList();
            var assignment = valid.Where(a => a.Created is null || slip.Created is null || a.Created <= slip.Created).MaxBy(a => (a.FromDate, a.Created));
            var current = valid.MaxBy(a => (a.FromDate, a.Created));
            if (assignment is not null && current is not null && current.Base != assignment.Base)
            {
                report.Warnings.Add($"{slip.Name} ({slip.Employee}): paid on base {assignment.Base:N2}; the base from {current.FromDate:yyyy-MM-dd} was later set to {current.Base:N2}, so {(current.Base > assignment.Base ? "back pay is due" : "an overpayment is to be recovered")}.");
            }

            if (assignment is null || !stored.TryGetValue(slip.Structure, out var structure))
            {
                differences.Add($"{slip.Name} ({slip.Employee}): {(assignment is null ? "no salary structure assignment on " + slip.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : $"salary structure \"{slip.Structure}\" is not in Gebeta")}.");
                continue;
            }

            var renames = SlipAbbreviations(slip, structure, renamed.GetValueOrDefault(slip.Structure));
            var result = await Query<CalculateSalaryQuery, SalaryCalculationDto>(Recalculation(slip, assignment.Base, structure, renames), cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                differences.Add($"{slip.Name} ({slip.Employee}): {result.Error!.Message}");
                continue;
            }

            var gebeta = result.Value.Lines.GroupBy(l => l.Abbreviation).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount), StringComparer.Ordinal);
            var wrong = slip.Rows.Where(r => !(r.Statistical && !r.IsDeduction))
                .GroupBy(r => renames[r.Abbreviation]).Select(g => (Abbr: g.Key, Erp: g.Sum(r => r.Amount), Gebeta: gebeta.GetValueOrDefault(g.Key)))
                .Where(c => c.Erp != c.Gebeta)
                .Select(c => $"{c.Abbr} Gebeta {c.Gebeta:N2}, ERPNext {c.Erp:N2}")
                .ToList();
            if (result.Value.NetPay != slip.NetPay)
            {
                wrong.Add($"net pay Gebeta {result.Value.NetPay:N2}, ERPNext {slip.NetPay:N2}");
            }

            if (wrong.Count == 0)
            {
                equal++;
            }
            else
            {
                differences.Add($"{slip.Name} ({slip.Employee}): {string.Join("; ", wrong)}");
            }
        }

        const int Shown = 25;
        report.Failures.AddRange(differences.Take(Shown));
        if (differences.Count > Shown)
        {
            report.Failures.Add($"... and {differences.Count - Shown} more slips differ.");
        }

        var period = $"{slips[0].StartDate:yyyy-MM-dd} – {slips[0].EndDate:yyyy-MM-dd}";
        report.CheckPassed = equal == slips.Count;
        report.Check = $"payroll {period}: {equal} of {slips.Count} slips recalculated by Gebeta equal ERPNext to the cent";
        return report;
    }

    /// <summary>What Gebeta stores for each migrated structure (in a dry run, what it would store).</summary>
    private async Task<Dictionary<string, IReadOnlyList<SalaryComponentDto>>> StructuresForCheckAsync(CancellationToken cancellationToken)
    {
        var erp = await source.SalaryStructuresAsync(options.Company, cancellationToken).ConfigureAwait(false);
        if (options.DryRun || _companyId == Guid.Empty)
        {
            return erp.ToDictionary(s => s.Name, s => (IReadOnlyList<SalaryComponentDto>)MapStructure(s).Components, StringComparer.Ordinal);
        }

        var ids = await idMap.LoadAsync(options.Company, "Salary Structure", cancellationToken).ConfigureAwait(false);
        var stored = (await Query<ListSalaryStructuresQuery, IReadOnlyList<SalaryStructureDto>>(new ListSalaryStructuresQuery(_companyId), cancellationToken).ConfigureAwait(false))
            .Value.ToDictionary(s => s.Id);
        return ids.Where(i => stored.ContainsKey(i.Value)).ToDictionary(i => i.Key, i => stored[i.Value].Components, StringComparer.Ordinal);
    }

    /// <summary>
    /// The slip's inputs for Gebeta's calculator. A component paid only through an Additional Salary (overtime, a one-off deduction)
    /// is not in the structure, so it joins the calculation at zero and receives its amount as an additional amount.
    /// </summary>
    public static CalculateSalaryQuery Recalculation(ErpSalarySlip slip, decimal baseAmount, IReadOnlyList<SalaryComponentDto> structure, IReadOnlyDictionary<string, string> renames)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(renames);
        var components = structure.ToList();
        var additional = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in slip.Rows.Where(r => r.FromAdditionalSalary || r.AdditionalAmount != 0m))
        {
            var abbreviation = renames[row.Abbreviation];
            if (components.All(c => c.Abbreviation != abbreviation))
            {
                components.Add(new SalaryComponentDto(row.Component, abbreviation, row.IsDeduction ? nameof(ComponentType.Deduction) : nameof(ComponentType.Earning),
                    Amount: 0m, IsTaxable: row.IsTaxable));
            }

            additional[abbreviation] = additional.GetValueOrDefault(abbreviation) + (row.AdditionalAmount != 0m ? row.AdditionalAmount : row.Amount);
        }

        return new CalculateSalaryQuery(new CalculateSalaryRequest(baseAmount, slip.EndDate, slip.PaymentDays, slip.TotalWorkingDays, components, additional));
    }

    /// <summary>Each slip row's abbreviation as Gebeta knows it: the structure's renames, then the same cleaning for components paid only as additional salary.</summary>
    public static Dictionary<string, string> SlipAbbreviations(ErpSalarySlip slip, IReadOnlyList<SalaryComponentDto> structure, IReadOnlyDictionary<string, string>? structureRenames)
    {
        ArgumentNullException.ThrowIfNull(slip);
        ArgumentNullException.ThrowIfNull(structure);
        var taken = structure.Select(c => c.Abbreviation).ToHashSet(StringComparer.Ordinal);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var abbreviation in slip.Rows.Select(r => r.Abbreviation).Distinct(StringComparer.Ordinal))
        {
            result[abbreviation] = structureRenames?.GetValueOrDefault(abbreviation)
                ?? (Codes.IsValidAbbreviation(abbreviation) ? abbreviation : Codes.Abbreviation(abbreviation, taken));
        }

        return result;
    }
}
