using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

/// <summary>An amount an assignment gives a salary structure's formulas, beside the base (e.g. ctc, fuel_payment).</summary>
public sealed record PayValue(string Name, decimal Amount);

/// <summary>
/// A company's saved salary structure. Its components are validated every time it is saved. <see cref="Variables"/> are the
/// pay values its formulas use besides the base (an allowance agreed per employee, a contract amount); each assignment gives them.
/// </summary>
public sealed class SalaryStructureDefinition : Entity
{
    private List<SalaryComponentLine> _components = [];
    private List<string> _variables = [];

    private SalaryStructureDefinition()
    {
        Name = string.Empty;
    }

    private SalaryStructureDefinition(Guid companyId, string name, List<SalaryComponentLine> components, List<string> variables)
    {
        CompanyId = companyId;
        Name = name;
        _components = components;
        _variables = variables;
    }

    public Guid CompanyId { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<SalaryComponentLine> Components => _components;
    public IReadOnlyList<string> Variables => _variables;

    public static Result<SalaryStructureDefinition> Create(Guid companyId, string name, IReadOnlyList<SalaryComponentLine> components, IReadOnlyList<string>? variables = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (companyId == Guid.Empty)
        {
            return Error.Validation("A salary structure belongs to a company.");
        }

        var names = (variables ?? []).Select(v => v?.Trim() ?? string.Empty).ToList();
        foreach (var v in names)
        {
            if (v.Length is 0 or > 40 || !(char.IsAsciiLetter(v[0]) || v[0] == '_') || !v.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return Error.Validation($"The pay value '{v}' must be a name of letters, digits and _ (e.g. ctc, fuel_payment).");
            }

            if (SalaryStructure.StandardVariables.Contains(v) || SalaryStructure.TotalVariables.Contains(v) || SalaryStructure.Functions.Contains(v)
                || components.Any(c => string.Equals(c.Abbreviation?.Trim(), v, StringComparison.Ordinal)))
            {
                return Error.Validation($"The pay value '{v}' has the name of a component or of something Gebeta already provides.");
            }
        }

        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
        {
            return Error.Validation("A pay value is listed twice.");
        }

        var compiled = SalaryStructure.Create(name, components, names);
        return compiled.IsFailure ? compiled.Error! : new SalaryStructureDefinition(companyId, compiled.Value.Name, components.ToList(), names);
    }

    public SalaryStructure Compile() => SalaryStructure.Create(Name, _components, _variables).Value;

    /// <summary>An assignment must give every pay value this structure uses, and nothing else.</summary>
    public Result CheckValues(IReadOnlyDictionary<string, decimal>? values)
    {
        var given = values ?? new Dictionary<string, decimal>();
        var missing = _variables.Where(v => !given.ContainsKey(v)).ToList();
        if (missing.Count > 0)
        {
            return Result.Failure(Error.Validation($"{Name} needs these pay values: {string.Join(", ", missing)}."));
        }

        var extra = given.Keys.Where(k => !_variables.Contains(k)).ToList();
        return extra.Count > 0 ? Result.Failure(Error.Validation($"{Name} does not use these pay values: {string.Join(", ", extra)}.")) : Result.Success();
    }

    public void Deactivate() => IsActive = false;
}

/// <summary>Which structure and base salary an employee has from a date (a raise is a new assignment).</summary>
public sealed class SalaryStructureAssignment : Entity, IAudited
{
    private SalaryStructureAssignment()
    {
    }

    private List<PayValue> _values = [];

    private SalaryStructureAssignment(Guid companyId, Guid employeeId, Guid structureId, decimal baseAmount, DateOnly fromDate)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        StructureId = structureId;
        Base = baseAmount;
        FromDate = fromDate;
    }

    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }

    /// <summary>Salary changes show in the employee's history.</summary>
    Guid? IAudited.HistoryOwnerId => EmployeeId;
    public Guid StructureId { get; private set; }
    public decimal Base { get; private set; }
    public DateOnly FromDate { get; private set; }

    /// <summary>The structure's pay values for this employee from <see cref="FromDate"/>; a change is a new assignment, like a raise.</summary>
    public IReadOnlyDictionary<string, decimal> Values => _values.ToDictionary(v => v.Name, v => v.Amount, StringComparer.Ordinal);

    public static Result<SalaryStructureAssignment> Create(Guid companyId, Guid employeeId, Guid structureId, decimal baseAmount, DateOnly fromDate,
        IReadOnlyDictionary<string, decimal>? values = null)
    {
        if (baseAmount < 0m)
        {
            return Error.Validation("The base salary cannot be negative.");
        }

        if (decimal.Round(baseAmount, 2) != baseAmount)
        {
            return Error.Validation("The base salary has at most 2 decimals (santim).");
        }

        return new SalaryStructureAssignment(companyId, employeeId, structureId, baseAmount, fromDate)
        {
            _values = (values ?? new Dictionary<string, decimal>()).OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => new PayValue(v.Key, v.Value)).ToList(),
        };
    }
}

/// <summary>
/// An amount paid on top of the structure's formulas in one month (overtime, a one-off allowance or deduction). Kept on the slip
/// so a back payment recalculating the month pays it again: it does not change with the base, but it changes the tax.
/// </summary>
public sealed record AdditionalPay(string Component, string Abbreviation, ComponentType Type, bool IsTaxable, decimal Amount);

/// <summary>
/// One stored line of a salary slip. <see cref="IsAdditional"/> lines come from outside the structure
/// (back payment arrears and their tax, recoveries) and are not taxed again in this month.
/// </summary>
public sealed record SalarySlipLine(string Component, string Abbreviation, ComponentType Type, decimal Amount, string? Formula, bool IsAdditional = false);

/// <summary>
/// A salary slip for one employee and one period. Calculated when created (draft), immutable once
/// submitted; cancelling keeps it for the record, and a corrected slip is a new draft.
/// </summary>
public sealed class SalarySlip : DocumentBase
{
    private List<SalarySlipLine> _lines = [];
    private List<AdditionalPay> _additional = [];

    private SalarySlip()
        : base()
    {
        EmployeeName = string.Empty;
        EmployeeNumber = string.Empty;
        StructureName = string.Empty;
        TaxTable = string.Empty;
    }

    private SalarySlip(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        EmployeeName = string.Empty;
        EmployeeNumber = string.Empty;
        StructureName = string.Empty;
        TaxTable = string.Empty;
    }

    public override string DocumentType => "Salary Slip";

    public Guid EmployeeId { get; private set; }
    public string EmployeeNumber { get; private set; }
    public string EmployeeName { get; private set; }
    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public Guid StructureId { get; private set; }
    public string StructureName { get; private set; }
    public decimal Base { get; private set; }
    public decimal PaymentDays { get; private set; }
    public decimal TotalWorkingDays { get; private set; }
    public decimal GrossPay { get; private set; }
    public decimal TaxableEarnings { get; private set; }
    public decimal TotalDeductions { get; private set; }
    public decimal NetPay { get; private set; }
    public decimal EmployerContributions { get; private set; }
    public string TaxTable { get; private set; }
    public IReadOnlyList<SalarySlipLine> Lines => _lines;

    /// <summary>
    /// Paid in the previous system and brought in at cut-over (docs/data-migration.md step 5), so back payments and reports see the
    /// months before Gebeta. It posts nothing (the opening balances already hold its effect) and cannot be cancelled here.
    /// </summary>
    public bool IsHistory { get; private set; }

    /// <summary>What was paid this month on top of the formulas (already inside <see cref="Lines"/>).</summary>
    public IReadOnlyList<AdditionalPay> Additional => _additional;

    public decimal AmountOf(string abbreviation) => _lines.Where(l => l.Abbreviation == abbreviation).Sum(l => l.Amount);

    /// <summary>Calculates a new draft slip. The tax table is the one valid on the last day of the period.</summary>
    public static SalarySlip Calculate(
        Guid companyId,
        Guid employeeId,
        string employeeNumber,
        string employeeName,
        DateOnly periodStart,
        DateOnly periodEnd,
        SalaryStructureDefinition structure,
        SalaryStructureAssignment assignment,
        decimal paymentDays,
        decimal totalWorkingDays,
        IReadOnlyDictionary<string, decimal>? additional,
        IncomeTaxTable taxTable,
        IReadOnlyList<SalarySlipLine>? extraLines = null,
        IReadOnlyList<AdditionalPay>? additionalPay = null)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(assignment);
        if (periodEnd < periodStart)
        {
            throw new DomainException("payroll.period", "The period cannot end before it starts.");
        }

        if (structure.CompanyId != companyId || assignment.CompanyId != companyId)
        {
            throw new DomainException("payroll.company_mismatch", "The employee's structure and assignment must belong to the same company.");
        }

        // What is paid on top this month goes into the calculation, so tax and pension see it: amounts for the structure's own
        // components (entered on the slip) and additional salary entries (overtime, allowances, one-off deductions).
        var onTop = (additional ?? new Dictionary<string, decimal>()).Where(a => a.Value != 0m)
            .Join(structure.Components, a => a.Key, c => c.Abbreviation, (a, c) => new AdditionalPay(c.Name, c.Abbreviation, c.Type, c.IsTaxable, a.Value))
            .Concat(additionalPay ?? [])
            .ToList();
        var result = SalaryCalculator.Calculate(structure.Compile().WithAdditional(onTop, structure.Variables),
            new SalaryInput(assignment.Base, paymentDays, totalWorkingDays, periodEnd,
                onTop.GroupBy(a => a.Abbreviation).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount)), assignment.Values), taxTable);
        var extras = extraLines ?? [];
        var extraEarnings = extras.Where(l => l.Type == ComponentType.Earning).Sum(l => l.Amount);
        var extraDeductions = extras.Where(l => l.Type == ComponentType.Deduction).Sum(l => l.Amount);
        var extraEmployer = extras.Where(l => l.Type == ComponentType.EmployerContribution).Sum(l => l.Amount);

        return new SalarySlip(companyId, periodEnd)
        {
            EmployeeId = employeeId,
            EmployeeNumber = employeeNumber,
            EmployeeName = employeeName,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            StructureId = structure.Id,
            StructureName = structure.Name,
            Base = assignment.Base,
            PaymentDays = paymentDays,
            TotalWorkingDays = totalWorkingDays,
            GrossPay = result.GrossPay + extraEarnings,
            TaxableEarnings = result.TaxableEarnings,
            TotalDeductions = result.TotalDeductions + extraDeductions,
            NetPay = result.NetPay + extraEarnings - extraDeductions,
            EmployerContributions = result.EmployerContributions + extraEmployer,
            TaxTable = result.TaxTable,
            _lines = result.Lines.Select(l => new SalarySlipLine(l.Component, l.Abbreviation, l.Type, l.Amount, l.Formula))
                .Concat(extras.Select(l => l with { IsAdditional = true }))
                .ToList(),
            _additional = onTop,
        };
    }

    /// <summary>
    /// A slip as the previous system paid it: the lines are kept exactly (not recalculated) under the old number, and it is
    /// submitted at once. <paramref name="taxableEarnings"/> comes with the lines because a line does not say whether it was taxed.
    /// </summary>
    public static SalarySlip Record(
        Guid companyId,
        Guid employeeId,
        string employeeNumber,
        string employeeName,
        DateOnly periodStart,
        DateOnly periodEnd,
        Guid structureId,
        string structureName,
        decimal baseAmount,
        decimal paymentDays,
        decimal totalWorkingDays,
        IReadOnlyList<SalarySlipLine> lines,
        decimal taxableEarnings,
        string number,
        IReadOnlyList<AdditionalPay>? additional = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        if (periodEnd < periodStart)
        {
            throw new DomainException("payroll.period", "The period cannot end before it starts.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("payroll.history_lines", "A salary slip from the previous system needs its lines.");
        }

        decimal Sum(ComponentType type) => lines.Where(l => l.Type == type).Sum(l => l.Amount);
        var slip = new SalarySlip(companyId, periodEnd)
        {
            EmployeeId = employeeId,
            EmployeeNumber = employeeNumber,
            EmployeeName = employeeName,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            StructureId = structureId,
            StructureName = structureName,
            Base = baseAmount,
            PaymentDays = paymentDays,
            TotalWorkingDays = totalWorkingDays,
            GrossPay = Sum(ComponentType.Earning),
            TaxableEarnings = taxableEarnings,
            TotalDeductions = Sum(ComponentType.Deduction),
            NetPay = Sum(ComponentType.Earning) - Sum(ComponentType.Deduction),
            EmployerContributions = Sum(ComponentType.EmployerContribution),
            TaxTable = EthiopianIncomeTax.All.FirstOrDefault(t => t.IsValidOn(periodEnd))?.Code ?? "history",
            IsHistory = true,
            _lines = lines.ToList(),
            _additional = (additional ?? []).ToList(),
        };
        slip.AssignName(number.Trim());
        slip.Submit();
        return slip;
    }

    protected override void OnCancel()
    {
        if (IsHistory)
        {
            throw new DomainException("payroll.history_cancel",
                $"{Name} was paid in the previous system and only kept as history; it cannot be cancelled. Correct a later month instead (a back payment or recovery).");
        }
    }

    protected override void ValidateForSubmit()
    {
        if (NetPay < 0m)
        {
            throw new DomainException("payroll.negative_net", $"Net pay of {EmployeeName} would be negative ({NetPay}); check the deductions.");
        }
    }
}
