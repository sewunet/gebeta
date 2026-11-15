using Gebeta.Payroll.Domain.BackPay;

namespace Gebeta.Payroll.Domain.Salary;

public interface ISalaryStructureRepository
{
    Task<SalaryStructureDefinition?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalaryStructureDefinition>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(Guid companyId, string name, CancellationToken cancellationToken);
    void Add(SalaryStructureDefinition structure);
}

public interface ISalaryStructureAssignmentRepository
{
    Task<IReadOnlyList<SalaryStructureAssignment>> ListForEmployeeAsync(Guid employeeId, CancellationToken cancellationToken);

    /// <summary>The assignment in force on <paramref name="date"/>: the latest one starting on or before it.</summary>
    Task<SalaryStructureAssignment?> InForceOnAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken);

    void Add(SalaryStructureAssignment assignment);
}

public interface ISalarySlipRepository
{
    Task<SalarySlip?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalarySlip>> ListAsync(Guid companyId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);

    /// <summary>True when the employee already has a draft or submitted slip overlapping the period.</summary>
    Task<bool> HasActiveSlipAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken);

    /// <summary>Submitted slips of the employee whose period starts between the two dates.</summary>
    Task<IReadOnlyList<SalarySlip>> SubmittedForEmployeeAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    void Add(SalarySlip slip);
}

public interface IBackPaymentRepository
{
    Task<SalaryBackPayment?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalaryBackPayment>> ListAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>Submitted back payments of the employee that cover any month between the two dates.</summary>
    Task<IReadOnlyList<SalaryBackPayment>> SubmittedOverlappingAsync(Guid employeeId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    void Add(SalaryBackPayment backPayment);
}

public interface IAdditionalPayRepository
{
    /// <summary>Entries waiting for the employee's slip whose period contains their payroll date.</summary>
    Task<IReadOnlyList<AdditionalPayEntry>> PendingAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken cancellationToken);

    Task<IReadOnlyList<AdditionalPayEntry>> ForSlipAsync(Guid salarySlipId, CancellationToken cancellationToken);
}
