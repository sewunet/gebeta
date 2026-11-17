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
    private PayContext? _pay;

    /// <summary>The structures as Gebeta will hold them and the ERPNext fields their pay values come from; read once per run.</summary>
    private sealed record PayContext(
        IReadOnlyDictionary<string, MappedStructure> Structures,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> AssignmentFields,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>> EmployeeFields);

    private async Task<PayContext> PayAsync(CancellationToken cancellationToken)
    {
        if (_pay is not null)
        {
            return _pay;
        }

        var scope = await source.FormulaScopeAsync(cancellationToken).ConfigureAwait(false);
        var structures = (await source.SalaryStructuresAsync(options.Company, cancellationToken).ConfigureAwait(false))
            .ToDictionary(s => s.Name, s => PayValues.Map(s, scope), StringComparer.Ordinal);
        string[] Fields(string from) => structures.Values.SelectMany(m => m.Sources).Where(s => s.Value == from).Select(s => s.Key).Distinct(StringComparer.Ordinal).ToArray();
        _pay = new PayContext(structures,
            await source.FieldValuesAsync(PayValues.FromAssignment, options.Company, Fields(PayValues.FromAssignment), cancellationToken).ConfigureAwait(false),
            await source.FieldValuesAsync(PayValues.FromEmployee, options.Company, Fields(PayValues.FromEmployee), cancellationToken).ConfigureAwait(false));
        return _pay;
    }

    /// <summary>Everyone still employed (status other than Left). The father's name falls back to the second word of the full name.</summary>
    private async Task<StepReport> EmployeesAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("employees");
        var all = await source.EmployeesAsync(options.Company, cancellationToken).ConfigureAwait(false);
        var employees = all.Where(e => e.Status != "Left").ToList();
        if (all.Count > employees.Count)
        {
            report.Warnings.Add($"{all.Count - employees.Count} employees who left are not migrated; their slips stay in ERPNext.");
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
        var structures = (await PayAsync(cancellationToken).ConfigureAwait(false)).Structures.Values.ToList();
        var known = await KnownAsync("Salary Structure", cancellationToken).ConfigureAwait(false);
        foreach (var mapped in structures)
        {
            var s = mapped;
            if (known.ContainsKey(s.Name))
            {
                report.AlreadyMigrated++;
                continue;
            }

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
                var check = SalaryStructure.Create(s.Name, lines, mapped.Variables.ToList());
                if (check.IsFailure)
                {
                    report.Failures.Add($"Salary structure \"{s.Name}\": {check.Error!.Message}");
                    continue;
                }

                report.Created++;
                continue;
            }

            var created = await Handle<CreateSalaryStructureCommand, SalaryStructureDto>(new CreateSalaryStructureCommand(_companyId, s.Name, mapped.Components, mapped.Variables), cancellationToken)
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

    /// <summary>The whole assignment history, oldest first, so back pay across the cut-over has the earlier bases.</summary>
    private async Task<StepReport> AssignmentsAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("assignments");
        var rows = await source.AssignmentsAsync(options.Company, cancellationToken).ConfigureAwait(false);
        var employee = await ResolverAsync("Employee", cancellationToken).ConfigureAwait(false);
        var structure = await ResolverAsync("Salary Structure", cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync("Salary Structure Assignment", cancellationToken).ConfigureAwait(false);
        var pay = await PayAsync(cancellationToken).ConfigureAwait(false);
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
                report.Failures.Add(pay.Structures.ContainsKey(a.Structure)
                    ? $"{a.Employee} from {a.FromDate:yyyy-MM-dd}: salary structure \"{a.Structure}\" is not migrated (see the structures step)."
                    : $"{a.Employee} from {a.FromDate:yyyy-MM-dd}: salary structure \"{a.Structure}\" has no earnings or deductions in ERPNext, or is not submitted; fix it there.");
                continue;
            }

            var (values, problems) = pay.Structures.TryGetValue(a.Structure, out var mapped)
                ? PayValues.For(a, mapped, pay.AssignmentFields, pay.EmployeeFields)
                : ([], []);
            if (problems.Count > 0)
            {
                report.Failures.AddRange(problems);
                continue;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<AssignSalaryStructureCommand, SalaryStructureAssignmentDto>(new AssignSalaryStructureCommand(employeeId, structureId, a.Base, a.FromDate, values), cancellationToken)
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
        var pay = await PayAsync(cancellationToken).ConfigureAwait(false);
        var bases = (await source.AssignmentsAsync(options.Company, cancellationToken).ConfigureAwait(false)).ToLookup(a => a.Employee, StringComparer.Ordinal);
        var equal = 0;
        var differences = new List<string>();
        foreach (var slip in slips)
        {
            var (assignment, current) = PaidWith(slip, bases[slip.Employee]);
            if (assignment is not null && current is not null && current.Base != assignment.Base)
            {
                report.Warnings.Add($"{slip.Name} ({slip.Employee}): paid on base {assignment.Base:N2}; the base from {current.FromDate:yyyy-MM-dd} was later set to {current.Base:N2}, so {(current.Base > assignment.Base ? "back pay is due" : "an overpayment is to be recovered")}.");
            }

            if (assignment is null || !stored.TryGetValue(slip.Structure, out var structure))
            {
                differences.Add($"{slip.Name} ({slip.Employee}): {(assignment is null ? "no salary structure assignment on " + slip.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : $"salary structure \"{slip.Structure}\" is not in Gebeta")}.");
                continue;
            }

            var mapped = pay.Structures.GetValueOrDefault(slip.Structure);
            var renames = SlipAbbreviations(slip, structure, mapped?.Renamed);
            var values = mapped is null ? [] : PayValues.For(assignment, mapped, pay.AssignmentFields, pay.EmployeeFields).Values;
            var result = await Query<CalculateSalaryQuery, SalaryCalculationDto>(Recalculation(slip, assignment.Base, structure, renames, values), cancellationToken).ConfigureAwait(false);
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
        if (options.DryRun || _companyId == Guid.Empty)
        {
            return (await PayAsync(cancellationToken).ConfigureAwait(false)).Structures.ToDictionary(s => s.Key, s => s.Value.Components, StringComparer.Ordinal);
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
    public static CalculateSalaryQuery Recalculation(ErpSalarySlip slip, decimal baseAmount, IReadOnlyList<SalaryComponentDto> structure, IReadOnlyDictionary<string, string> renames,
        IReadOnlyDictionary<string, decimal>? values = null)
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

        return new CalculateSalaryQuery(new CalculateSalaryRequest(baseAmount, slip.EndDate, slip.PaymentDays, slip.TotalWorkingDays, components, additional,
            values is { Count: > 0 } ? values : null));
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

    /// <summary>
    /// The assignment a slip was paid with, and the one valid for its period now. They differ when a raise was entered after the
    /// slip was made (a backdated raise): that is back pay, not part of the slip.
    /// </summary>
    private static (ErpAssignment? PaidWith, ErpAssignment? Now) PaidWith(ErpSalarySlip slip, IEnumerable<ErpAssignment> assignments)
    {
        var valid = assignments.Where(a => a.FromDate <= slip.StartDate).ToList();
        return (valid.Where(a => a.Created is null || slip.Created is null || a.Created <= slip.Created).MaxBy(a => (a.FromDate, a.Created)),
            valid.MaxBy(a => (a.FromDate, a.Created)));
    }

    /// <summary>
    /// Salary slips paid in ERPNext up to the cut-over (docs/data-migration.md step 5), kept as history so back payments and reports
    /// see those months. Lines are kept as paid, under the old slip number; the recorded net pay must equal ERPNext's or the slip is
    /// not recorded. Nothing is posted: the opening balances already hold those months.
    /// </summary>
    private async Task<StepReport> SlipHistoryAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("history");
        var to = options.Cutover ?? DateOnly.MaxValue;
        var slips = await source.SlipsAsync(options.Company, options.HistoryFrom, to, cancellationToken).ConfigureAwait(false);
        var employee = await ResolverAsync("Employee", cancellationToken).ConfigureAwait(false);
        var structure = await ResolverAsync("Salary Structure", cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync("Salary Slip", cancellationToken).ConfigureAwait(false);
        var pay = await PayAsync(cancellationToken).ConfigureAwait(false);
        var bases = (await source.AssignmentsAsync(options.Company, cancellationToken).ConfigureAwait(false)).ToLookup(a => a.Employee, StringComparer.Ordinal);
        var expected = 0;
        var forLeavers = 0;
        decimal net = 0m;
        foreach (var slip in slips)
        {
            if (employee(slip.Employee) is not { } employeeId)
            {
                forLeavers++;
                continue;
            }

            expected++;
            if (known.ContainsKey(slip.Name))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var mapped = pay.Structures.GetValueOrDefault(slip.Structure);
            var renames = SlipAbbreviations(slip, mapped?.Components ?? [], mapped?.Renamed);
            var lines = new List<SalaryLineDto>();
            var additional = new List<SlipAdditionalDto>();
            decimal taxable = 0m;
            foreach (var row in slip.Rows)
            {
                if (row.Statistical && !row.IsDeduction)
                {
                    continue; // worked out, never paid
                }

                var type = !row.IsDeduction ? ComponentType.Earning : row.Statistical || row.NotInTotal ? ComponentType.EmployerContribution : ComponentType.Deduction;
                lines.Add(new SalaryLineDto(row.Component, renames[row.Abbreviation], type.ToString(), row.Amount, null));
                var onTop = row.AdditionalAmount != 0m ? row.AdditionalAmount : row.FromAdditionalSalary ? row.Amount : 0m;
                if (onTop != 0m)
                {
                    additional.Add(new SlipAdditionalDto(row.Component, renames[row.Abbreviation], type.ToString(), row.IsTaxable, onTop));
                }

                if (type == ComponentType.Earning && row.IsTaxable)
                {
                    taxable += row.Amount;
                }
            }

            decimal Sum(ComponentType t) => lines.Where(l => l.Type == t.ToString()).Sum(l => l.Amount);
            var recordedNet = Sum(ComponentType.Earning) - Sum(ComponentType.Deduction);
            if (recordedNet != slip.NetPay)
            {
                report.Failures.Add($"{slip.Name} ({slip.Employee}): its lines give net pay {recordedNet:N2} but ERPNext paid {slip.NetPay:N2}; not recorded.");
                continue;
            }

            var paidWith = PaidWith(slip, bases[slip.Employee]).PaidWith;
            if (options.DryRun)
            {
                report.Created++;
                net += slip.NetPay;
                continue;
            }

            var recorded = await Handle<RecordSalarySlipHistoryCommand, SalarySlipDto>(new RecordSalarySlipHistoryCommand(employeeId, slip.StartDate, slip.EndDate,
                structure(slip.Structure), slip.Structure, paidWith?.Base ?? 0m, slip.PaymentDays, slip.TotalWorkingDays, lines, taxable, slip.Name, additional), cancellationToken)
                .ConfigureAwait(false);
            if (recorded.IsFailure)
            {
                report.Failures.Add($"{slip.Name} ({slip.Employee}): {recorded.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Salary Slip", slip.Name, recorded.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
            net += slip.NetPay;
        }

        if (forLeavers > 0)
        {
            report.Warnings.Add($"{forLeavers} slips of employees who are not migrated (left, or failed the employees step) are not brought.");
        }

        var done = report.Created + report.AlreadyMigrated;
        report.CheckPassed = done == expected;
        report.Check = $"{(options.DryRun ? "dry run: " : "")}{done} of {expected} slips" + (report.Created > 0 ? $" (this run: net pay {net:N2}, equal to ERPNext slip by slip)" : "");
        return report;
    }
}
