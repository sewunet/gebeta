using System.Globalization;
using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Gebeta.Payroll.Infrastructure.Persistence;

/// <summary>
/// Employee loans (not standing deductions) with at most the threshold's number of instalments left, so payroll knows the deduction is about to stop
/// (docs/requirements/sap-features.md #9). The instalments left are the balance divided by the instalment, rounded up. A paused loan is not about to
/// end, so it is left out until it resumes. A loan announced once is not announced again as its instalments run down: it is the same loan.
/// </summary>
internal sealed class LoansNearlyRepaidCondition(PayrollDbContext db) : IAlertCondition
{
    public string Id => "payroll.loans-nearly-repaid";
    public string Name => "Employee loans nearly repaid";
    public string Description => "Loans being recovered through payroll that have only a few instalments left.";
    public string RequiredPermission => PayrollPermissions.LoansRead;
    public AlertThreshold? Threshold { get; } = new("instalments", 2, 1, 24);

    public async Task<IReadOnlyList<AlertItem>> EvaluateAsync(AlertContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The balance comes from the repayments kept on each loan, so it is worked out here; the loans a company recovers at one time are few.
        var loans = await db.Loans.AsNoTracking()
            .Where(l => l.CompanyId == context.CompanyId && l.Kind == LoanKind.Loan && l.DocStatus == DocStatus.Submitted && !l.IsPaused)
            .OrderBy(l => l.Name).ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = new List<AlertItem>();
        foreach (var loan in loans)
        {
            if (loan.Balance is not > 0m)
            {
                continue;
            }

            var left = (int)Math.Ceiling(loan.Balance.Value / loan.Instalment);
            if (left > context.Threshold)
            {
                continue;
            }

            var title = string.Create(CultureInfo.InvariantCulture, $"{loan.EmployeeName} ({loan.EmployeeNumber}): {loan.Name}, {left} instalment{(left == 1 ? string.Empty : "s")} left");
            var body = string.Create(CultureInfo.InvariantCulture, $"{loan.Balance.Value:N2} still to recover, {loan.Instalment:N2} a month.");
            items.Add(new AlertItem(loan.Id.ToString("N"), title, body, "EmployeeLoan", loan.Id, NotificationSeverity.Info));
        }

        return items.Take(AlertLimits.MaxItems).ToList();
    }
}
