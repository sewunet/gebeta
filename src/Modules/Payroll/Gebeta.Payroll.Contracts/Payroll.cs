namespace Gebeta.Payroll.Contracts;

/// <summary>Permissions defined by the Payroll module.</summary>
public static class PayrollPermissions
{
    public const string CalculatorUse = "payroll.calculator.use";
    public const string TaxTablesRead = "payroll.tax-tables.read";

    public static IReadOnlyList<string> All { get; } = [CalculatorUse, TaxTablesRead];
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
