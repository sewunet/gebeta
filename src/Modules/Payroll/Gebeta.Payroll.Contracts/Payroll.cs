namespace Gebeta.Payroll.Contracts;

/// <summary>Permissions defined by the Payroll module.</summary>
public static class PayrollPermissions
{
    public const string CalculatorUse = "payroll.calculator.use";
    public const string TaxTablesRead = "payroll.tax-tables.read";
    public const string StructuresRead = "payroll.structures.read";
    public const string StructuresManage = "payroll.structures.manage";
    public const string AssignmentsRead = "payroll.assignments.read";
    public const string AssignmentsManage = "payroll.assignments.manage";
    public const string SlipsRead = "payroll.salary-slips.read";
    public const string SlipsCreate = "payroll.salary-slips.create";
    public const string SlipsSubmit = "payroll.salary-slips.submit";
    public const string SlipsCancel = "payroll.salary-slips.cancel";
    public const string BackPaymentsRead = "payroll.back-payments.read";
    public const string BackPaymentsCreate = "payroll.back-payments.create";
    public const string BackPaymentsSubmit = "payroll.back-payments.submit";
    public const string BackPaymentsCancel = "payroll.back-payments.cancel";

    public static IReadOnlyList<string> All { get; } =
        [CalculatorUse, TaxTablesRead, StructuresRead, StructuresManage, AssignmentsRead, AssignmentsManage, SlipsRead, SlipsCreate, SlipsSubmit, SlipsCancel,
         BackPaymentsRead, BackPaymentsCreate, BackPaymentsSubmit, BackPaymentsCancel];
}

/// <summary>A salary component. Type: Earning, Deduction or EmployerContribution.</summary>
public sealed record SalaryComponentDto(
    string Name,
    string Abbreviation,
    string Type,
    string? Formula = null,
    decimal? Amount = null,
    string? Condition = null,
    bool IsTaxable = true,
    bool DependsOnPaymentDays = false);

/// <summary>
/// Calculate one salary without saving anything. When <see cref="Components"/> is empty the standard
/// Ethiopian structure is used (basic salary, income tax, 7% employee and 11% employer pension).
/// </summary>
public sealed record CalculateSalaryRequest(
    decimal Base,
    DateOnly PayrollDate,
    decimal PaymentDays = 30,
    decimal TotalWorkingDays = 30,
    IReadOnlyList<SalaryComponentDto>? Components = null,
    IReadOnlyDictionary<string, decimal>? Additional = null,
    IReadOnlyDictionary<string, decimal>? Variables = null);

public sealed record SalaryLineDto(string Component, string Abbreviation, string Type, decimal Amount, string? Formula);

public sealed record SalaryCalculationDto(
    IReadOnlyList<SalaryLineDto> Lines,
    decimal GrossPay,
    decimal TaxableEarnings,
    decimal TotalDeductions,
    decimal NetPay,
    decimal EmployerContributions,
    string TaxTable);

public sealed record TaxSlabDto(decimal Over, decimal? UpTo, decimal RatePercent, decimal Deduction);

public sealed record TaxTableDto(string Code, string Name, DateOnly ValidFrom, DateOnly? ValidTo, string LegalSource, IReadOnlyList<TaxSlabDto> Slabs);

public sealed record SalaryStructureDto(Guid Id, Guid CompanyId, string Name, bool IsActive, IReadOnlyList<SalaryComponentDto> Components);

public sealed record SalaryStructureAssignmentDto(Guid Id, Guid EmployeeId, Guid StructureId, decimal Base, DateOnly FromDate);

public sealed record SalarySlipDto(
    Guid Id,
    string? Number,
    Guid CompanyId,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string EthiopianPeriod,
    string Status,
    string StructureName,
    decimal Base,
    decimal PaymentDays,
    decimal TotalWorkingDays,
    IReadOnlyList<SalaryLineDto> Lines,
    decimal GrossPay,
    decimal TaxableEarnings,
    decimal TotalDeductions,
    decimal NetPay,
    decimal EmployerContributions,
    string TaxTable);

/// <summary>Line of a submitted slip in the integration event (Accounting builds the payroll journal from these).</summary>
public sealed record SalarySlipLineData(string Abbreviation, string Component, string Type, decimal Amount);

/// <summary>Published when a salary slip is submitted. Accounting (or the ERPNext connector) posts the journal.</summary>
public sealed record SalarySlipSubmitted(
    Guid SlipId,
    string Number,
    Guid CompanyId,
    Guid EmployeeId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrossPay,
    decimal TotalDeductions,
    decimal NetPay,
    decimal EmployerContributions,
    IReadOnlyList<SalarySlipLineData> Lines,
    DateTimeOffset OccurredAt);

/// <summary>Published when a submitted salary slip is cancelled; the journal is reversed.</summary>
public sealed record SalarySlipCancelled(Guid SlipId, string Number, Guid CompanyId, DateTimeOffset OccurredAt);

/// <summary>
/// Capability: payment days for an employee in a period. HR provides it from attendance and unpaid leave;
/// without HR, Payroll uses the employee's joining and leaving dates. Null means "no information".
/// </summary>
public interface IPaymentDaysProvider
{
    Task<decimal?> GetPaymentDaysAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, decimal totalWorkingDays, CancellationToken cancellationToken);
}

public sealed record BackPaymentMonthDto(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string EthiopianMonth,
    string Source,
    string TaxTable,
    decimal OldGross,
    decimal NewGross,
    decimal OldTax,
    decimal NewTax,
    decimal GrossArrears,
    decimal AdditionalTax,
    decimal OtherDeductionsArrears,
    decimal EmployerContributionsArrears,
    decimal NetArrears);

public sealed record AdditionalPayDto(string Name, string Abbreviation, string Type, decimal Amount, Guid? SalarySlipId);

public sealed record SalaryBackPaymentDto(
    Guid Id,
    string? Number,
    Guid CompanyId,
    Guid EmployeeId,
    string EmployeeNumber,
    string EmployeeName,
    decimal NewBase,
    DateOnly EffectiveFrom,
    DateOnly EffectiveTo,
    DateOnly PayrollDate,
    string Reason,
    string? Reference,
    string Status,
    IReadOnlyList<BackPaymentMonthDto> Months,
    decimal TotalGrossArrears,
    decimal TotalAdditionalTax,
    decimal TotalOtherDeductions,
    decimal TotalEmployerContributions,
    decimal NetArrears,
    IReadOnlyList<AdditionalPayDto> Entries);

/// <summary>Published when a back payment is submitted; its amounts are paid in the payroll month.</summary>
public sealed record SalaryBackPaymentSubmitted(Guid BackPaymentId, string Number, Guid CompanyId, Guid EmployeeId, DateOnly PayrollDate,
    decimal GrossArrears, decimal AdditionalTax, decimal NetArrears, DateTimeOffset OccurredAt);

public sealed record SalaryBackPaymentCancelled(Guid BackPaymentId, string Number, Guid CompanyId, DateTimeOffset OccurredAt);
