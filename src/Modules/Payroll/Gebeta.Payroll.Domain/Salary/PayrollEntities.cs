using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

/// <summary>A company's saved salary structure. Its components are validated every time it is saved.</summary>
public sealed class SalaryStructureDefinition : Entity
{
    private List<SalaryComponentLine> _components = [];

    private SalaryStructureDefinition()
    {
        Name = string.Empty;
    }

    private SalaryStructureDefinition(Guid companyId, string name, List<SalaryComponentLine> components)
    {
        CompanyId = companyId;
        Name = name;
        _components = components;
    }

    public Guid CompanyId { get; private set; }
    public string Name { get; private set; }
    public bool IsActive { get; private set; } = true;
    public IReadOnlyList<SalaryComponentLine> Components => _components;

    public static Result<SalaryStructureDefinition> Create(Guid companyId, string name, IReadOnlyList<SalaryComponentLine> components)
    {
        if (companyId == Guid.Empty)
        {
            return Error.Validation("A salary structure belongs to a company.");
        }

        var compiled = SalaryStructure.Create(name, components);
        return compiled.IsFailure ? compiled.Error! : new SalaryStructureDefinition(companyId, compiled.Value.Name, components.ToList());
    }

    public SalaryStructure Compile() => SalaryStructure.Create(Name, _components).Value;

    public void Deactivate() => IsActive = false;
}

/// <summary>Which structure and base salary an employee has from a date (a raise is a new assignment).</summary>
public sealed class SalaryStructureAssignment : Entity
{
    private SalaryStructureAssignment()
    {
    }

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
    public Guid StructureId { get; private set; }
    public decimal Base { get; private set; }
    public DateOnly FromDate { get; private set; }

    public static Result<SalaryStructureAssignment> Create(Guid companyId, Guid employeeId, Guid structureId, decimal baseAmount, DateOnly fromDate)
    {
        if (baseAmount < 0m)
        {
            return Error.Validation("The base salary cannot be negative.");
        }

        if (decimal.Round(baseAmount, 2) != baseAmount)
        {
            return Error.Validation("The base salary has at most 2 decimals (santim).");
        }

        return new SalaryStructureAssignment(companyId, employeeId, structureId, baseAmount, fromDate);
    }
}

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
        IReadOnlyList<SalarySlipLine>? extraLines = null)
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

        var result = SalaryCalculator.Calculate(structure.Compile(),
            new SalaryInput(assignment.Base, paymentDays, totalWorkingDays, periodEnd, additional), taxTable);
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
        };
    }

    protected override void ValidateForSubmit()
    {
        if (NetPay < 0m)
        {
            throw new DomainException("payroll.negative_net", $"Net pay of {EmployeeName} would be negative ({NetPay}); check the deductions.");
        }
    }
}
