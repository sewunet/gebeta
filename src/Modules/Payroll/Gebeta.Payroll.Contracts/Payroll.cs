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
    public const string ProfilesManage = "payroll.profiles.manage";
    public const string RunsRead = "payroll.runs.read";
    public const string RunsManage = "payroll.runs.manage";
    public const string ReportsRead = "payroll.reports.read";

    public const string AdditionalRead = "payroll.additional-salary.read";
    public const string AdditionalManage = "payroll.additional-salary.manage";
    public const string LoansRead = "payroll.loans.read";
    public const string LoansManage = "payroll.loans.manage";

    /// <summary>Self-service: an employee sees their own submitted payslips.</summary>
    public const string SelfRead = "payroll.self.read";

    public static IReadOnlyList<string> All { get; } =
        [CalculatorUse, TaxTablesRead, StructuresRead, StructuresManage, AssignmentsRead, AssignmentsManage, SlipsRead, SlipsCreate, SlipsSubmit, SlipsCancel,
         BackPaymentsRead, BackPaymentsCreate, BackPaymentsSubmit, BackPaymentsCancel, ProfilesManage, RunsRead, RunsManage, ReportsRead, SelfRead, AdditionalRead, AdditionalManage, LoansRead, LoansManage];
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

/// <param name="Variables">Pay values the formulas use besides the base; every assignment gives them.</param>
public sealed record SalaryStructureDto(Guid Id, Guid CompanyId, string Name, bool IsActive, IReadOnlyList<SalaryComponentDto> Components, IReadOnlyList<string>? Variables = null);

public sealed record SalaryStructureAssignmentDto(Guid Id, Guid EmployeeId, Guid StructureId, decimal Base, DateOnly FromDate, IReadOnlyDictionary<string, decimal>? Values = null);

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
    string TaxTable,
    bool IsHistory = false);

/// <summary>Line of a submitted slip in the integration event (Accounting builds the payroll journal from these).</summary>
public sealed record SalarySlipLineData(string Abbreviation, string Component, string Type, decimal Amount);

/// <summary>Published when a salary slip is submitted. Accounting (or an external ledger connector) posts the journal.</summary>
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

public sealed record PayrollProfileDto(Guid EmployeeId, string PaymentMethod, string? BankName, string? BankAccountNumber, string? PensionNumber);

public sealed record PayrollRunIssueDto(Guid EmployeeId, string EmployeeName, string Reason);

public sealed record PayrollRunDto(
    Guid Id,
    string? Number,
    Guid CompanyId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string Status,
    bool SlipsGenerated,
    int SlipCount,
    decimal TotalGross,
    decimal TotalNet,
    IReadOnlyList<PayrollRunIssueDto> Issues);

/// <summary>One employee in the monthly employment income tax declaration.</summary>
public sealed record IncomeTaxRow(string EmployeeNumber, string EmployeeName, string? Tin, DateOnly DateOfJoining, decimal BasicSalary,
    decimal TaxableEarnings, decimal NonTaxableEarnings, decimal TaxWithheld, decimal EmployeePension, decimal NetPay);

public sealed record IncomeTaxDeclarationDto(string CompanyName, string CompanyTin, DateOnly PeriodStart, DateOnly PeriodEnd, string EthiopianMonth,
    IReadOnlyList<IncomeTaxRow> Rows, decimal TotalTaxableEarnings, decimal TotalTaxWithheld, int Employees);

public sealed record PensionRow(string EmployeeNumber, string EmployeeName, string? PensionNumber, string? Tin, decimal BasicSalary,
    decimal EmployeeContribution, decimal EmployerContribution, decimal Total);

public sealed record PensionScheduleDto(string CompanyName, string CompanyTin, DateOnly PeriodStart, DateOnly PeriodEnd, string EthiopianMonth,
    IReadOnlyList<PensionRow> Rows, decimal TotalEmployee, decimal TotalEmployer, decimal Total);

public sealed record BankTransferRow(string EmployeeNumber, string EmployeeName, string? BankName, string? AccountNumber, decimal NetPay);

public sealed record BankTransferDto(string CompanyName, DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<BankTransferRow> BankTransfers,
    IReadOnlyList<BankTransferRow> Cash, IReadOnlyList<string> Issues, decimal TotalBank, decimal TotalCash);

/// <summary>An amount paid on top of the formulas in one month (overtime, a one-off allowance or deduction).</summary>
public sealed record SlipAdditionalDto(string Component, string Abbreviation, string Type, bool IsTaxable, decimal Amount);

public sealed record AdditionalSalaryLineDto(Guid EmployeeId, string EmployeeNumber, string EmployeeName, decimal Amount, string? Note);

/// <param name="InSlips">How many of the employees' amounts are already in a salary slip.</param>
public sealed record AdditionalSalaryDto(Guid Id, string? Number, Guid CompanyId, DateOnly PayrollDate, string Component, string Abbreviation, string Type,
    bool IsTaxable, string Status, string? Remark, IReadOnlyList<AdditionalSalaryLineDto> Lines, decimal Total, int InSlips);

/// <summary>One employee's month in the payroll comparison.</summary>
public sealed record PayFigures(decimal Gross, decimal IncomeTax, decimal EmployeePension, decimal EmployerPension, decimal Net, decimal PaymentDays);

/// <param name="Status">Changed, New, Missing or Same.</param>
/// <param name="ChangePercent">The change in net pay, in percent; null when last month's net was not above zero.</param>
/// <param name="Reasons">What the data says changed: additional salary, back pay, payment days, a new assignment, leaving.</param>
public sealed record PayComparisonRow(Guid EmployeeId, string EmployeeNumber, string EmployeeName, PayFigures? Previous, PayFigures? Current,
    decimal NetChange, decimal? ChangePercent, string Status, bool Flagged, IReadOnlyList<string> Reasons);

public sealed record PayTotals(int Headcount, decimal Gross, decimal IncomeTax, decimal EmployeePension, decimal EmployerPension, decimal Net);

/// <summary>A payroll run against the company's previous month, before it is submitted.</summary>
/// <param name="PreviousPeriodEnd">The day before this period starts; there was no previous month when <see cref="HasPrevious"/> is false.</param>
public sealed record PayrollComparisonDto(Guid RunId, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly PreviousPeriodEnd, bool HasPrevious,
    decimal ThresholdPercent, IReadOnlyList<PayComparisonRow> Rows, PayTotals PreviousTotals, PayTotals CurrentTotals, int FlaggedCount);

public sealed record LoanRepaymentDto(DateOnly Date, decimal Amount, string Source, string? Reference, bool InSlip);

/// <summary>
/// An employee loan or standing deduction. Status: Draft, Running, Paused, Completed (repaid or stopped) or Cancelled. Balance is what is
/// still owed on a loan after the instalments slips have taken (drafts included); none for a standing deduction.
/// </summary>
public sealed record EmployeeLoanDto(Guid Id, string? Number, Guid CompanyId, Guid EmployeeId, string EmployeeNumber, string EmployeeName, string Kind,
    string Component, string Abbreviation, decimal? Principal, decimal Instalment, DateOnly StartMonth, DateOnly? EndMonth, string Status, decimal Recovered,
    decimal? Balance, string? Remark, IReadOnlyList<LoanRepaymentDto> Repayments);

/// <summary>One employee failing one payroll check. Severity: Error or Warning.</summary>
public sealed record PayrollCheckRow(Guid EmployeeId, string EmployeeNumber, string EmployeeName, Guid SlipId, string? SlipNumber, string Check, string Severity,
    string Message);

public sealed record PayrollCheckCount(string Check, string Severity, int Employees);

/// <summary>A payroll run's checks before submit: every failure, a count per check, and how many employees are flagged at all.</summary>
public sealed record PayrollChecksDto(Guid RunId, DateOnly PeriodStart, DateOnly PeriodEnd, int SlipsChecked, decimal MinimumNetSharePercent,
    IReadOnlyList<PayrollCheckCount> Counts, IReadOnlyList<PayrollCheckRow> Rows, int Errors, int Warnings, int FlaggedEmployees);

