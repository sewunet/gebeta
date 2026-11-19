using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Domain.Salary;

public enum PaymentMethod
{
    BankTransfer,
    Cash,
}

/// <summary>
/// Payroll's own details for an employee (docs/plugin-architecture.md: modules extend Core's employee
/// in their own tables): how salary is paid and the pension registration number.
/// </summary>
public sealed class EmployeePayrollProfile : Entity, IAudited
{
    private EmployeePayrollProfile()
    {
    }

    private EmployeePayrollProfile(Guid companyId, Guid employeeId)
    {
        CompanyId = companyId;
        EmployeeId = employeeId;
    }

    public Guid CompanyId { get; private set; }
    public Guid EmployeeId { get; private set; }

    /// <summary>Changes show in the employee's history.</summary>
    Guid? IAudited.HistoryOwnerId => EmployeeId;
    public PaymentMethod PaymentMethod { get; private set; } = PaymentMethod.BankTransfer;
    public string? BankName { get; private set; }
    public string? BankAccountNumber { get; private set; }

    /// <summary>Registration number at the pension agency (private or public servants).</summary>
    public string? PensionNumber { get; private set; }

    /// <summary>Where the employee's salary cost is reported (a Core cost centre); none means the company's Main one.</summary>
    public Guid? CostCenterId { get; private set; }

    /// <summary>Sets the cost centre; the caller checks it is an active cost centre of the employee's company.</summary>
    public void SetCostCenter(Guid? costCenterId) => CostCenterId = costCenterId;

    public static EmployeePayrollProfile Create(Guid companyId, Guid employeeId) => new(companyId, employeeId);

    public Result Update(PaymentMethod method, string? bankName, string? accountNumber, string? pensionNumber)
    {
        var account = accountNumber?.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
        // A bank transfer may wait for its account (as an employee with no profile does); the payroll checks flag it until it is entered.
        // An account, once given, needs its bank.
        if (method == PaymentMethod.BankTransfer && !string.IsNullOrWhiteSpace(account) && string.IsNullOrWhiteSpace(bankName))
        {
            return Result.Failure(Error.Validation("Name the bank the account is at."));
        }

        if (account is { Length: > 0 } && (account.Length is < 6 or > 30 || !account.All(char.IsAsciiDigit)))
        {
            return Result.Failure(Error.Validation("The account number must be 6 to 30 digits."));
        }

        PaymentMethod = method;
        BankName = string.IsNullOrWhiteSpace(bankName) ? null : bankName.Trim();
        BankAccountNumber = string.IsNullOrWhiteSpace(account) ? null : account;
        PensionNumber = string.IsNullOrWhiteSpace(pensionNumber) ? null : pensionNumber.Trim();
        return Result.Success();
    }
}

public interface IPayrollProfileRepository
{
    Task<EmployeePayrollProfile?> GetAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EmployeePayrollProfile>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    void Add(EmployeePayrollProfile profile);
}
