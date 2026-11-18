using Gebeta.Payroll.Domain.BackPay;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

/// <summary>A loan the employee repays (the balance runs down to zero), or a standing deduction paid on to someone else until it is stopped.</summary>
public enum LoanKind
{
    Loan,
    StandingDeduction,
}

/// <summary>An amount taken off the loan: by a salary slip (<see cref="EntryId"/>) or repaid another way (cash, settlement).</summary>
public sealed record LoanRepayment(DateOnly Date, decimal Amount, string Source, Guid? EntryId, string? Reference);

/// <summary>
/// An employee loan or standing deduction, recorded once (docs/requirements/competitive-features.md #5): salary advances, cost-sharing,
/// cooperative or Idir contributions, court orders. From the start month each salary slip deducts the instalment (the last one only what
/// is left of a loan) until the balance is zero, the end month passes, or it is settled. It can be paused and resumed. Instalments are a
/// line of their own on the slip, after tax: they do not change taxable pay. A loan's instalments credit Employee Advances; a standing
/// deduction's credit Other Payroll Deductions, to be paid on.
/// </summary>
public sealed class EmployeeLoan : DocumentBase
{
    public const string Source = "Employee Loan";

    /// <summary>Slip lines of loan instalments carry this abbreviation, so the payroll journal credits the employee advances account.</summary>
    public const string LoanAbbreviation = "LOAN";

    public const string FromPayroll = "Payroll";
    public const string Manual = "Manual";

    private List<LoanRepayment> _repayments = [];

    private EmployeeLoan()
    {
        EmployeeNumber = string.Empty;
        EmployeeName = string.Empty;
        Component = string.Empty;
        Abbreviation = string.Empty;
    }

    private EmployeeLoan(Guid companyId, DateOnly startMonth)
        : base(companyId, startMonth)
    {
        EmployeeNumber = string.Empty;
        EmployeeName = string.Empty;
        Component = string.Empty;
        Abbreviation = string.Empty;
    }

    public override string DocumentType => Source;

    public Guid EmployeeId { get; private set; }
    public string EmployeeNumber { get; private set; }
    public string EmployeeName { get; private set; }
    public LoanKind Kind { get; private set; }
    public string Component { get; private set; }

    /// <summary>The slip line's abbreviation: <see cref="LoanAbbreviation"/> for loans, the deduction's own (e.g. IDIR) otherwise.</summary>
    public string Abbreviation { get; private set; }

    /// <summary>A loan's amount; none for a standing deduction.</summary>
    public decimal? Principal { get; private set; }

    public decimal Instalment { get; private set; }

    /// <summary>The first payroll period is the one containing this date.</summary>
    public DateOnly StartMonth { get; private set; }

    /// <summary>A standing deduction's last payroll period is the one containing this date, if it has one; set when it is stopped.</summary>
    public DateOnly? EndMonth { get; private set; }

    public bool IsPaused { get; private set; }

    /// <summary>A standing deduction was stopped (it still deducts up to <see cref="EndMonth"/>).</summary>
    public bool IsStopped { get; private set; }
    public string? Remark { get; private set; }
    public IReadOnlyList<LoanRepayment> Repayments => _repayments;
    public decimal Recovered => _repayments.Sum(r => r.Amount);

    /// <summary>What is still owed on a loan; none for a standing deduction.</summary>
    public decimal? Balance => Principal - Recovered;

    /// <summary>A loan repaid in full, or a standing deduction stopped.</summary>
    public bool IsCompleted => Kind == LoanKind.Loan ? Balance <= 0m : IsStopped;

    public static EmployeeLoan Create(Guid companyId, Guid employeeId, string employeeNumber, string employeeName, LoanKind kind, string component,
        string? abbreviation, decimal? principal, decimal instalment, DateOnly startMonth, DateOnly? endMonth, string? remark)
    {
        if (string.IsNullOrWhiteSpace(component))
        {
            throw new DomainException("payroll.loan_component", "Name the deduction, e.g. Salary advance or Idir.");
        }

        if (instalment <= 0m || decimal.Round(instalment, 2) != instalment)
        {
            throw new DomainException("payroll.loan_instalment", "The monthly instalment is a positive amount in birr and santim.");
        }

        var abbr = kind == LoanKind.Loan ? LoanAbbreviation : abbreviation?.Trim().ToUpperInvariant() ?? string.Empty;
        if (kind == LoanKind.Loan)
        {
            if (principal is not > 0m || decimal.Round(principal.Value, 2) != principal)
            {
                throw new DomainException("payroll.loan_principal", "A loan needs its amount in birr and santim.");
            }

            if (instalment > principal)
            {
                throw new DomainException("payroll.loan_instalment", "The instalment is more than the loan.");
            }

            if (endMonth is not null)
            {
                throw new DomainException("payroll.loan_end", "A loan ends when it is repaid; only a standing deduction has an end month.");
            }
        }
        else
        {
            if (principal is not null)
            {
                throw new DomainException("payroll.loan_principal", "A standing deduction has no total; record a loan instead.");
            }

            if (abbr.Length is 0 or > 20 || !char.IsAsciiLetter(abbr[0]) || !abbr.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') || abbr == LoanAbbreviation)
            {
                throw new DomainException("payroll.loan_abbreviation", "Give the deduction a short name of letters, digits and _ (e.g. IDIR), other than LOAN.");
            }

            if (endMonth < startMonth)
            {
                throw new DomainException("payroll.loan_end", "The end month is before the start month.");
            }
        }

        return new EmployeeLoan(companyId, startMonth)
        {
            EmployeeId = employeeId,
            EmployeeNumber = employeeNumber,
            EmployeeName = employeeName,
            Kind = kind,
            Component = component.Trim(),
            Abbreviation = abbr,
            Principal = principal,
            Instalment = instalment,
            StartMonth = startMonth,
            EndMonth = endMonth,
            Remark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim(),
        };
    }

    /// <summary>
    /// What the slip for this period deducts, or 0: nothing before the start month, after the end month, while paused, once repaid, or when
    /// this period already has its instalment (a cancelled slip's entry waits for the corrected slip). Changes nothing.
    /// </summary>
    public decimal DueFor(DateOnly periodStart, DateOnly periodEnd)
    {
        if (DocStatus != DocStatus.Submitted || IsPaused || Balance <= 0m || periodEnd < StartMonth || (EndMonth is { } end && periodStart > end)
            || _repayments.Any(r => r.Source == FromPayroll && r.Date >= periodStart && r.Date <= periodEnd))
        {
            return 0m;
        }

        return Kind == LoanKind.Loan ? Math.Min(Instalment, Balance!.Value) : Instalment;
    }

    /// <summary>The slip's entry for this period's instalment, recorded as repaid; call only once the slip is calculated.</summary>
    public AdditionalPayEntry TakeInstalment(DateOnly periodStart, DateOnly periodEnd)
    {
        var amount = DueFor(periodStart, periodEnd);
        if (amount <= 0m)
        {
            throw new DomainException("payroll.loan_not_due", $"{Name} has nothing due for this period.");
        }

        var entry = AdditionalPayEntry.Create(CompanyId, EmployeeId, periodEnd, Component, Abbreviation, ComponentType.Deduction, amount, Source, Id, Name ?? string.Empty);
        _repayments.Add(new LoanRepayment(periodEnd, amount, FromPayroll, entry.Id, null));
        return entry;
    }

    public void Pause()
    {
        RequireRunning();
        IsPaused = !IsPaused ? true : throw new DomainException("payroll.loan_paused", $"{Name} is already paused.");
    }

    public void Resume()
    {
        RequireRunning();
        IsPaused = IsPaused ? false : throw new DomainException("payroll.loan_running", $"{Name} is not paused.");
    }

    /// <summary>
    /// Ends it early. A loan: the rest of the balance was repaid another way (cash, a bank transfer; record the receipt in Cash or Accounting).
    /// A standing deduction: stops after the month of <paramref name="date"/>.
    /// </summary>
    public void Settle(DateOnly date, string? reference)
    {
        RequireRunning();
        if (Kind == LoanKind.Loan)
        {
            _repayments.Add(new LoanRepayment(date, Balance!.Value, Manual, null, string.IsNullOrWhiteSpace(reference) ? null : reference.Trim()));
        }
        else
        {
            if (date < StartMonth)
            {
                throw new DomainException("payroll.loan_end", "The deduction cannot stop before it starts; cancel it instead.");
            }

            EndMonth = EndMonth is { } end && end < date ? end : date;
            IsStopped = true;
        }

        IsPaused = false;
    }

    /// <summary>Cancels it and the instalments no slip has taken; refused once a slip includes one (settle it instead).</summary>
    public void CancelWithEntries(IReadOnlyList<AdditionalPayEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Any(e => e.SalarySlipId is not null && !e.IsCancelled))
        {
            throw new DomainException("payroll.loan_in_slips", $"Salary slips already deducted {Name}; settle or stop it instead of cancelling.");
        }

        Cancel();
        foreach (var entry in entries)
        {
            entry.Cancel();
        }
    }

    private void RequireRunning()
    {
        if (DocStatus != DocStatus.Submitted || IsCompleted)
        {
            throw new DomainException("payroll.loan_not_running", $"{Name ?? "This loan"} is not running.");
        }
    }
}
