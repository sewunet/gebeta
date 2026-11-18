using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.BackPay;

/// <summary>
/// An amount to add to an employee's salary slip for a payroll month, outside the salary structure:
/// back payment arrears, the tax and pension on them, or a recovery. Taken into the first slip whose
/// period contains <see cref="PayrollDate"/>; released again if that slip is cancelled.
/// </summary>
public sealed class AdditionalPayEntry : Entity
{
    private AdditionalPayEntry()
    {
        Name = string.Empty;
        Abbreviation = string.Empty;
        SourceType = string.Empty;
    }

    private AdditionalPayEntry(Guid companyId, Guid employeeId, DateOnly payrollDate, string name, string abbreviation, ComponentType type,
        decimal amount, string sourceType, Guid sourceId, string sourceNumber)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
        PayrollDate = payrollDate;
        Name = name;
        Abbreviation = abbreviation;
        Type = type;
        Amount = amount;
        SourceType = sourceType;
        SourceId = sourceId;
        SourceNumber = sourceNumber;
    }

    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }
    public DateOnly PayrollDate { get; private set; }
    public string Name { get; private set; }
    public string Abbreviation { get; private set; }
    public ComponentType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string SourceType { get; private set; }
    public Guid SourceId { get; private set; }
    public string? SourceNumber { get; private set; }

    /// <summary>The salary slip that includes this entry, if any.</summary>
    public Guid? SalarySlipId { get; private set; }

    public bool IsCancelled { get; private set; }

    /// <summary>
    /// Goes into the month's calculation, so tax and pension see it (additional salary: overtime, allowances, one-off deductions).
    /// Otherwise it is a line of its own, already taxed where it came from (back payment arrears and their tax).
    /// </summary>
    public bool InCalculation { get; private set; }

    /// <summary>For entries in the calculation: whether income tax applies to it.</summary>
    public bool IsTaxable { get; private set; }

    internal static AdditionalPayEntry ForCalculation(Guid companyId, Guid employeeId, DateOnly payrollDate, string name, string abbreviation,
        ComponentType type, bool isTaxable, decimal amount, string sourceType, Guid sourceId, string sourceNumber)
    {
        var entry = Create(companyId, employeeId, payrollDate, name, abbreviation, type, amount, sourceType, sourceId, sourceNumber);
        entry.InCalculation = true;
        entry.IsTaxable = isTaxable;
        return entry;
    }

    public AdditionalPay ToAdditionalPay() => new(Name, Abbreviation, Type, IsTaxable, Amount);

    internal static AdditionalPayEntry Create(Guid companyId, Guid employeeId, DateOnly payrollDate, string name, string abbreviation,
        ComponentType type, decimal amount, string sourceType, Guid sourceId, string sourceNumber) =>
        new(companyId, employeeId, payrollDate, name, abbreviation, type, Rounding.ToCurrency(amount), sourceType, sourceId, sourceNumber);

    public void IncludeIn(Guid salarySlipId)
    {
        if (IsCancelled || SalarySlipId is not null)
        {
            throw new DomainException("payroll.additional_taken", $"{Name} is already included in a salary slip or cancelled.");
        }

        SalarySlipId = salarySlipId;
    }

    /// <summary>The slip was cancelled: the entry waits for the corrected slip.</summary>
    public void Release() => SalarySlipId = null;

    internal void Cancel() => IsCancelled = true;

    public SalarySlipLine ToSlipLine() => new($"{Name} ({SourceNumber})", Abbreviation, Type, Amount, null, IsAdditional: true);
}

/// <summary>What to do when a revision lowers the salary (net arrears below zero).</summary>
public enum NegativeArrearsPolicy
{
    /// <summary>Deduct the overpayment in the payroll month (default).</summary>
    RecoverInPayroll,

    /// <summary>Refuse the back payment; the overpayment is settled another way.</summary>
    Refuse,
}

/// <summary>
/// Salary Back Payment: arrears of a retroactive salary change, paid in a later payroll with the tax
/// allocated to the months it belongs to (same behaviour as the current customized_forcommon app).
/// </summary>
public sealed class SalaryBackPayment : DocumentBase
{
    public const string Source = "Salary Back Payment";

    private List<BackPaymentMonth> _months = [];
    private List<AdditionalPayEntry> _entries = [];

    private SalaryBackPayment()
    {
        EmployeeName = string.Empty;
        EmployeeNumber = string.Empty;
        Reason = string.Empty;
    }

    private SalaryBackPayment(Guid companyId, DateOnly postingDate)
        : base(companyId, postingDate)
    {
        EmployeeName = string.Empty;
        EmployeeNumber = string.Empty;
        Reason = string.Empty;
    }

    public override string DocumentType => Source;

    public Guid EmployeeId { get; private set; }
    public string EmployeeNumber { get; private set; }
    public string EmployeeName { get; private set; }
    public Guid AssignmentId { get; private set; }
    public decimal NewBase { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly EffectiveTo { get; private set; }

    /// <summary>The arrears are paid with the slip of the month containing this date.</summary>
    public DateOnly PayrollDate { get; private set; }

    public string Reason { get; private set; }
    public string? Reference { get; private set; }
    public IReadOnlyList<BackPaymentMonth> Months => _months;
    public IReadOnlyList<AdditionalPayEntry> Entries => _entries;

    public decimal TotalGrossArrears => _months.Sum(m => m.GrossArrears);
    public decimal TotalAdditionalTax => _months.Sum(m => m.AdditionalTax);
    public decimal TotalOtherDeductions => _months.Sum(m => m.OtherDeductionsArrears);
    public decimal TotalEmployerContributions => _months.Sum(m => m.EmployerContributionsArrears);
    public decimal NetArrears => _months.Sum(m => m.NetArrears);

    public static SalaryBackPayment Create(Guid companyId, Guid employeeId, string employeeNumber, string employeeName, Guid assignmentId, decimal newBase,
        DateOnly effectiveFrom, DateOnly effectiveTo, DateOnly payrollDate, string reason, string? reference, IReadOnlyList<BackPaymentMonth> months)
    {
        ArgumentNullException.ThrowIfNull(months);
        if (effectiveTo < effectiveFrom)
        {
            throw new DomainException("payroll.back_payment_period", "The back payment period cannot end before it starts.");
        }

        if (payrollDate <= effectiveTo)
        {
            throw new DomainException("payroll.back_payment_payroll_date", "The arrears are paid in a payroll after the back payment period.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("payroll.back_payment_reason", "A reason is required (for example the approval of the increment).");
        }

        return new SalaryBackPayment(companyId, payrollDate)
        {
            EmployeeId = employeeId,
            EmployeeNumber = employeeNumber,
            EmployeeName = employeeName,
            AssignmentId = assignmentId,
            NewBase = newBase,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            PayrollDate = payrollDate,
            Reason = reason.Trim(),
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            _months = months.ToList(),
        };
    }

    /// <summary>Submits and creates the additional pay entries for the payroll month.</summary>
    public IReadOnlyList<AdditionalPayEntry> SubmitWithEntries(NegativeArrearsPolicy policy)
    {
        if (TotalGrossArrears == 0m && TotalAdditionalTax == 0m && TotalOtherDeductions == 0m)
        {
            throw new DomainException("payroll.back_payment_nothing", "Nothing to pay: these months were already paid at this salary.");
        }

        if (NetArrears < 0m && policy == NegativeArrearsPolicy.Refuse)
        {
            throw new DomainException("payroll.back_payment_negative", $"The revision lowers pay by {-NetArrears}; recovery in payroll is not allowed by the settings.");
        }

        Submit();

        AdditionalPayEntry Entry(string name, string abbreviation, ComponentType type, decimal amount) =>
            AdditionalPayEntry.Create(CompanyId, EmployeeId, PayrollDate, name, abbreviation, type, amount, Source, Id, Name ?? string.Empty);

        if (TotalGrossArrears > 0m)
        {
            _entries.Add(Entry("Back Payment Arrears", "BP_ARR", ComponentType.Earning, TotalGrossArrears));
        }
        else if (TotalGrossArrears < 0m)
        {
            _entries.Add(Entry("Back Payment Recovery", "BP_REC", ComponentType.Deduction, -TotalGrossArrears));
        }

        // Tax and pension differences: positive ones are deducted, negative ones are refunded as earnings.
        AddDifference("Back Payment Income Tax", "BP_TAX", TotalAdditionalTax);
        AddDifference("Back Payment Deductions", "BP_DED", TotalOtherDeductions);
        if (TotalEmployerContributions != 0m)
        {
            _entries.Add(Entry("Back Payment Employer Contributions", "BP_EMP", ComponentType.EmployerContribution, TotalEmployerContributions));
        }

        return _entries;

        void AddDifference(string name, string abbreviation, decimal difference)
        {
            if (difference > 0m)
            {
                _entries.Add(Entry(name, abbreviation, ComponentType.Deduction, difference));
            }
            else if (difference < 0m)
            {
                _entries.Add(Entry(name + " Refund", abbreviation + "_REF", ComponentType.Earning, -difference));
            }
        }
    }

    protected override void OnCancel()
    {
        if (_entries.Any(e => e.SalarySlipId is not null))
        {
            throw new DomainException("payroll.back_payment_paid", "The arrears are already in a salary slip; cancel that slip first.");
        }

        _entries.ForEach(e => e.Cancel());
    }
}
