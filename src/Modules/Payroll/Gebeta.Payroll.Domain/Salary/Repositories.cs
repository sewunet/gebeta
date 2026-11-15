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

    void Add(SalarySlip slip);
}
