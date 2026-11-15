using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

/// <summary>An employee the run could not pay, with the reason.</summary>
public sealed record PayrollRunIssue(Guid EmployeeId, string EmployeeName, string Reason);

/// <summary>
/// Monthly payroll for a whole company: prepares a draft slip for every employee employed in the period
/// who has a salary assignment, then submits them together.
/// </summary>
public sealed class PayrollRun : DocumentBase
{
    private List<Guid> _slipIds = [];
    private List<PayrollRunIssue> _issues = [];

    private PayrollRun()
    {
    }

    private PayrollRun(Guid companyId, DateOnly periodEnd)
        : base(companyId, periodEnd)
    {
    }

    public override string DocumentType => "Payroll Run";

    public DateOnly PeriodStart { get; private set; }
    public DateOnly PeriodEnd { get; private set; }
    public IReadOnlyList<Guid> SlipIds => _slipIds;
    public IReadOnlyList<PayrollRunIssue> Issues => _issues;
    public bool SlipsGenerated { get; private set; }

    public static PayrollRun Create(Guid companyId, DateOnly periodStart, DateOnly periodEnd)
    {
        if (periodEnd < periodStart || periodEnd.DayNumber - periodStart.DayNumber > 40)
        {
            throw new DomainException("payroll.run_period", "A payroll run covers one month.");
        }

        return new PayrollRun(companyId, periodEnd) { PeriodStart = periodStart, PeriodEnd = periodEnd };
    }

    public void RecordGenerated(IEnumerable<Guid> slipIds, IEnumerable<PayrollRunIssue> issues)
    {
        EnsureDraft();
        _slipIds = slipIds.ToList();
        _issues = issues.ToList();
        SlipsGenerated = true;
    }

    protected override void ValidateForSubmit()
    {
        if (!SlipsGenerated || _slipIds.Count == 0)
        {
            throw new DomainException("payroll.run_empty", "Generate the salary slips before submitting the payroll run.");
        }
    }
}

public interface IPayrollRunRepository
{
    Task<PayrollRun?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayrollRun>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(PayrollRun run);
}
