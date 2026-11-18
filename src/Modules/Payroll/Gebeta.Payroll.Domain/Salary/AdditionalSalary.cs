using Gebeta.Payroll.Domain.BackPay;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

/// <summary>One employee's amount on an additional salary.</summary>
public sealed record AdditionalSalaryLine(Guid EmployeeId, string EmployeeNumber, string EmployeeName, decimal Amount, string? Note);

/// <summary>
/// An amount paid on top of salary for one payroll month, for many employees at once: overtime, an allowance, a one-off deduction
/// such as a cooperative or loan repayment. Submitting it gives each employee an entry that their slip for that month takes into the
/// calculation, so income tax and pension see it. Cancelling releases the entries, unless a slip already includes one.
/// </summary>
public sealed class AdditionalSalary : DocumentBase
{
    public const string Source = "Additional Salary";

    private List<AdditionalSalaryLine> _lines = [];

    private AdditionalSalary()
        : base()
    {
        Component = string.Empty;
        Abbreviation = string.Empty;
    }

    private AdditionalSalary(Guid companyId, DateOnly payrollDate)
        : base(companyId, payrollDate)
    {
        Component = string.Empty;
        Abbreviation = string.Empty;
    }

    public override string DocumentType => Source;

    public DateOnly PayrollDate { get; private set; }
    public string Component { get; private set; }
    public string Abbreviation { get; private set; }
    public ComponentType Type { get; private set; }
    public bool IsTaxable { get; private set; }
    public string? Remark { get; private set; }
    public IReadOnlyList<AdditionalSalaryLine> Lines => _lines;
    public decimal Total => _lines.Sum(l => l.Amount);

    public static AdditionalSalary Create(Guid companyId, DateOnly payrollDate, string component, string abbreviation, ComponentType type, bool isTaxable,
        IReadOnlyList<AdditionalSalaryLine> lines, string? remark)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var abbr = abbreviation?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(component))
        {
            throw new DomainException("payroll.additional_component", "Name the component, e.g. Overtime.");
        }

        if (abbr.Length is 0 or > 20 || !(char.IsAsciiLetter(abbr[0]) || abbr[0] == '_') || !abbr.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
        {
            throw new DomainException("payroll.additional_abbreviation", "The abbreviation is a short name of letters, digits and _ (e.g. OT).");
        }

        if (type is not (ComponentType.Earning or ComponentType.Deduction))
        {
            throw new DomainException("payroll.additional_type", "An additional salary is an earning or a deduction.");
        }

        if (lines.Count == 0 || lines.Any(l => l.Amount <= 0m || decimal.Round(l.Amount, 2) != l.Amount))
        {
            throw new DomainException("payroll.additional_lines", "List the employees, each with a positive amount in birr and santim.");
        }

        if (lines.Select(l => l.EmployeeId).Distinct().Count() != lines.Count)
        {
            throw new DomainException("payroll.additional_duplicate", "Each employee appears once; put their whole amount on one line.");
        }

        return new AdditionalSalary(companyId, payrollDate)
        {
            PayrollDate = payrollDate,
            Component = component.Trim(),
            Abbreviation = abbr,
            Type = type,
            IsTaxable = type == ComponentType.Earning && isTaxable,
            Remark = remark?.Trim(),
            _lines = lines.ToList(),
        };
    }

    /// <summary>Submits and gives each employee their entry for the month's slip.</summary>
    public IReadOnlyList<AdditionalPayEntry> SubmitWithEntries()
    {
        Submit();
        return _lines.Select(l => AdditionalPayEntry.ForCalculation(CompanyId, l.EmployeeId, PayrollDate, Component, Abbreviation, Type, IsTaxable, l.Amount,
            Source, Id, Name ?? string.Empty)).ToList();
    }

    /// <summary>Cancels and releases the entries; refused while a slip includes one (cancel that slip first, or correct next month).</summary>
    public void CancelWithEntries(IReadOnlyList<AdditionalPayEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var paid = entries.Where(e => e.SalarySlipId is not null).ToList();
        if (paid.Count > 0)
        {
            throw new DomainException("payroll.additional_paid",
                $"{paid.Count} of these amounts are already in salary slips; cancel those slips first, or correct it in a later month.");
        }

        Cancel();
        foreach (var entry in entries)
        {
            entry.Cancel();
        }
    }
}
