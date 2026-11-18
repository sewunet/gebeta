using System.Globalization;
using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Application;

public sealed record PayrollChecksQuery(Guid RunId) : IQuery<PayrollChecksDto>;

/// <summary>
/// Fixed checks on a payroll run's slips before submit (docs/requirements/sap-features.md #1, after SAP's Payroll Control Center): the
/// problems a person should look at whatever last month looked like, which the run comparison cannot show. Advice only: the run can still
/// be submitted. Read in three batches (slips, payroll profiles, employees), never per employee.
/// </summary>
public sealed class PayrollChecksHandler(
    IPayrollRunRepository runs,
    ISalarySlipRepository slips,
    IPayrollProfileRepository profiles,
    IEmployeeList employees,
    ICompanyAccess access,
    SlipFigures figures,
    PayrollOptions options) : IQueryHandler<PayrollChecksQuery, PayrollChecksDto>
{
    public const string Error = "Error";
    public const string Warning = "Warning";

    public const string NegativeNet = "Net pay below zero";
    public const string LowNet = "Net pay below the minimum share of gross";
    public const string NoBankAccount = "No bank account";
    public const string SharedBankAccount = "Bank account shared with another employee";
    public const string NoPensionNumber = "No pension number";
    public const string NoTin = "No TIN";
    public const string NoBase = "No base salary";
    public const string PaidWithoutDays = "Paid with no payment days";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<Result<PayrollChecksDto>> Handle(PayrollChecksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await runs.GetAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null || !await access.CanAccessAsync(run.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Gebeta.SharedKernel.Error.NotFound("Payroll run", query.RunId);
        }

        var current = (await slips.GetManyAsync(run.SlipIds.ToList(), cancellationToken).ConfigureAwait(false))
            .Where(s => s.DocStatus != DocStatus.Cancelled).OrderBy(s => s.EmployeeNumber, StringComparer.Ordinal).ToList();
        var profileOf = (await profiles.ListAsync(run.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.EmployeeId);
        var tinOf = (await employees.ListAsync(run.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(e => e.Id, e => e.Tin);

        // Bank accounts used by more than one employee of the run, whoever they are.
        var sharedAccounts = current.Select(s => s.EmployeeId).Distinct()
            .Select(id => (Id: id, Account: profileOf.GetValueOrDefault(id)?.BankAccountNumber))
            .Where(x => x.Account is not null).GroupBy(x => x.Account!)
            .Where(g => g.Count() > 1).ToDictionary(g => g.Key, g => g.Count());

        var rows = new List<PayrollCheckRow>();
        var minimumShare = options.MinimumNetSharePercent;
        foreach (var slip in current)
        {
            void Flag(string check, string severity, string message) =>
                rows.Add(new PayrollCheckRow(slip.EmployeeId, slip.EmployeeNumber, slip.EmployeeName, slip.Id, slip.Name, check, severity, message));

            var profile = profileOf.GetValueOrDefault(slip.EmployeeId);
            if (slip.NetPay < 0m)
            {
                Flag(NegativeNet, Error, $"Net pay {Money(slip.NetPay)}. {OtherDeductions(slip)}".TrimEnd());
            }
            else if (slip.GrossPay > 0m && slip.NetPay < slip.GrossPay * minimumShare / 100m)
            {
                Flag(LowNet, Warning, $"Net {Money(slip.NetPay)} of gross {Money(slip.GrossPay)} ({slip.NetPay / slip.GrossPay * 100m:0.#}%). {OtherDeductions(slip)}".TrimEnd());
            }

            // No profile means the default, a bank transfer, with no account yet.
            if ((profile?.PaymentMethod ?? PaymentMethod.BankTransfer) == PaymentMethod.BankTransfer)
            {
                if (profile?.BankAccountNumber is not { } account)
                {
                    Flag(NoBankAccount, Warning, "Paid by bank transfer, but the payroll profile has no account number.");
                }
                else if (sharedAccounts.TryGetValue(account, out var holders))
                {
                    Flag(SharedBankAccount, Warning, $"Account {account} is on {holders} employees of this run.");
                }
            }

            if (profile?.PensionNumber is null && figures.EmployeePension(slip) > 0m)
            {
                Flag(NoPensionNumber, Warning, $"Employee pension {Money(figures.EmployeePension(slip))} is deducted, but there is no pension number.");
            }

            if (string.IsNullOrWhiteSpace(tinOf.GetValueOrDefault(slip.EmployeeId)) && figures.IncomeTax(slip) > 0m)
            {
                Flag(NoTin, Warning, $"Income tax {Money(figures.IncomeTax(slip))} is withheld, but the employee has no TIN for the declaration.");
            }

            if (slip.Base <= 0m)
            {
                Flag(NoBase, Warning, "The salary structure assignment has no base salary.");
            }

            if (slip.PaymentDays <= 0m && slip.GrossPay > 0m)
            {
                Flag(PaidWithoutDays, Warning, $"Gross {Money(slip.GrossPay)} with 0 payment days.");
            }
        }

        var counts = rows.GroupBy(r => (r.Check, r.Severity))
            .Select(g => new PayrollCheckCount(g.Key.Check, g.Key.Severity, g.Select(r => r.EmployeeId).Distinct().Count()))
            .OrderBy(c => c.Severity == Error ? 0 : 1).ThenByDescending(c => c.Employees).ToList();
        rows = rows.OrderBy(r => r.Severity == Error ? 0 : 1).ThenBy(r => r.Check, StringComparer.Ordinal).ThenBy(r => r.EmployeeNumber, StringComparer.Ordinal).ToList();
        return new PayrollChecksDto(run.Id, run.PeriodStart, run.PeriodEnd, current.Count, minimumShare, counts, rows,
            rows.Count(r => r.Severity == Error), rows.Count(r => r.Severity == Warning), rows.Select(r => r.EmployeeId).Distinct().Count());
    }

    /// <summary>The deductions besides income tax and pension (loans, court orders, cooperatives), so the cause of a low net is visible.</summary>
    private string OtherDeductions(SalarySlip slip)
    {
        var others = slip.Lines.Where(l => l.Type == ComponentType.Deduction && !figures.IsIncomeTax(l) && !figures.IsEmployeePension(l) && l.Amount != 0m)
            .Select(l => $"{l.Component} {Money(l.Amount)}").ToList();
        return others.Count == 0 ? string.Empty : $"Other deductions: {string.Join("; ", others)}.";
    }

    private static string Money(decimal amount) => amount.ToString("N2", Invariant);
}
