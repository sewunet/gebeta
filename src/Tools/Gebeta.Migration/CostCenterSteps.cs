using Gebeta.Core.Application.CostCenters;
using Gebeta.Core.Contracts;
using Gebeta.Payroll.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;

namespace Gebeta.Migration;

/// <summary>Cost centres, and each employee's payroll details (payment method, bank account, cost centre, pension number).</summary>
public sealed partial class MigrationRun
{
    /// <summary>
    /// The company's cost centres that take postings. Its default becomes Gebeta's Main; the others keep their number as the code (number and name
    /// when several share the number), or a code from their name. Gebeta keeps one level: ERPNext's groups are not migrated (no posting can name a group).
    /// </summary>
    private async Task<StepReport> CostCentersAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("costcenters");
        var centers = await source.CostCentersAsync(options.Company, cancellationToken).ConfigureAwait(false);
        var known = await KnownAsync("Cost Center", cancellationToken).ConfigureAwait(false);
        var existing = options.DryRun || _companyId == Guid.Empty ? []
            : (await Query<ListCostCentersQuery, IReadOnlyList<CostCenterDto>>(new ListCostCentersQuery(_companyId), cancellationToken).ConfigureAwait(false)).Value.ToList();
        var used = existing.Select(c => c.Code).ToHashSet(StringComparer.Ordinal);
        var fallback = 0;
        // A number shared by several cost centres would give CODE, CODE-2, CODE-3: those take their name with the number instead.
        var sharedNumbers = centers.Where(c => c.Number is not null).GroupBy(c => c.Number!, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var c in centers)
        {
            if (known.ContainsKey(c.Name))
            {
                report.AlreadyMigrated++;
                continue;
            }

            if (c.IsCompanyDefault)
            {
                if (!options.DryRun)
                {
                    await idMap.AddAsync(options.Company, "Cost Center", c.Name, existing.Single(x => x.IsMain).Id, cancellationToken).ConfigureAwait(false);
                }

                report.Adopted++; // the company's Main, made with the company
                continue;
            }

            // Gebeta codes are ASCII; a name in Amharic (or anything that cleans up to nothing) gets CC-1, CC-2...
            var code = Codes.Normalise(c.Number is null ? c.CostCenterName : sharedNumbers.Contains(c.Number) ? $"{c.Number} {c.CostCenterName}" : c.Number, 20);
            if (code.Length == 0 || !code.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
            {
                code = $"CC-{++fallback}";
                report.Warnings.Add($"{c.Name}: no usable code from its number or name; it becomes {code}.");
            }

            var unique = Codes.Unique(code, used, 20);
            if (unique != code)
            {
                report.Warnings.Add($"{c.Name}: {code} is taken by another cost centre; this one becomes {unique}.");
                code = unique;
            }

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var created = await Handle<CreateCostCenterCommand, CostCenterDto>(new CreateCostCenterCommand(_companyId, code, c.CostCenterName), cancellationToken)
                .ConfigureAwait(false);
            if (created.IsFailure)
            {
                report.Failures.Add($"{c.Name}: {created.Error!.Message}");
                continue;
            }

            await idMap.AddAsync(options.Company, "Cost Center", c.Name, created.Value.Id, cancellationToken).ConfigureAwait(false);
            report.Created++;
        }

        await CheckAsync(report, "Cost Center", centers.Count, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// Each migrated employee's payroll details. Bank → bank transfer, with the bank details when the bank is named and the account is 6 to 30
    /// digits once spaces and dashes are removed (otherwise they wait for a person); Cash → cash; Cheque → cash, with a warning (Gebeta has no
    /// cheque payroll yet). A cost centre of another company, or a group, is left
    /// out with a warning. Warnings name the employee, never the account number. Saved again only when something differs, so a rerun writes nothing.
    /// </summary>
    private async Task<StepReport> PayrollProfilesAsync(CancellationToken cancellationToken)
    {
        var report = new StepReport("profiles");
        var employee = await ResolverAsync("Employee", cancellationToken).ConfigureAwait(false);
        var center = await ResolverAsync("Cost Center", cancellationToken).ConfigureAwait(false);
        var profiles = await source.PayrollProfilesAsync(options.Company, cancellationToken).ConfigureAwait(false);
        int considered = 0, cheque = 0;

        foreach (var p in profiles)
        {
            if (employee(p.Employee) is not { } employeeId)
            {
                continue; // left, or not migrated: no profile either
            }

            var account = p.BankAccount is null ? null : new string(p.BankAccount.Where(ch => !char.IsWhiteSpace(ch) && ch is not ('-' or '.' or '/')).ToArray());
            var mode = p.SalaryMode?.Trim().ToUpperInvariant();
            PaymentMethod? method = mode switch
            {
                "BANK" => PaymentMethod.BankTransfer,
                "CASH" => PaymentMethod.Cash,
                "CHEQUE" => PaymentMethod.Cash,
                _ when account is { Length: > 0 } => PaymentMethod.BankTransfer,
                _ => null,
            };
            Guid? costCenter = p.PayrollCostCenter is null ? null : center(p.PayrollCostCenter);
            if (p.PayrollCostCenter is not null && costCenter is null)
            {
                report.Warnings.Add($"{p.Employee}: payroll cost centre {p.PayrollCostCenter} is a group or another company's; the employee stays on Main.");
            }

            if (method is null && costCenter is null && p.PensionId is null)
            {
                continue; // nothing recorded in ERPNext: Gebeta's default (bank transfer, nothing filled in) is the same
            }

            considered++;
            if (mode == "CHEQUE")
            {
                cheque++;
            }

            // Bank details that cannot be used are left out (never shown in the report): the profile is a bank transfer waiting for its account,
            // which the payroll checks flag until a person enters it. The cost centre and pension number still come over.
            if (method == PaymentMethod.BankTransfer && (p.BankName is null || account is null || account.Length is < 6 or > 30 || !account.All(char.IsAsciiDigit)))
            {
                report.Warnings.Add($"{p.Employee}: paid by bank, but the bank name or the account number is missing or not 6 to 30 digits; "
                    + "enter them on the employee's Payment section.");
                account = null;
            }

            var bank = method == PaymentMethod.Cash || account is null ? null : p.BankName;
            var command = new UpdatePayrollProfileCommand(employeeId, method ?? PaymentMethod.BankTransfer, bank, bank is null ? null : account, p.PensionId,
                costCenter == Guid.Empty ? null : costCenter);

            if (options.DryRun)
            {
                report.Created++;
                continue;
            }

            var current = await Query<GetPayrollProfileQuery, PayrollProfileDto>(new GetPayrollProfileQuery(employeeId), cancellationToken).ConfigureAwait(false);
            if (current.IsSuccess && current.Value == new PayrollProfileDto(employeeId, command.PaymentMethod.ToString(), command.BankName, command.BankAccountNumber,
                    command.PensionNumber, command.CostCenterId))
            {
                report.AlreadyMigrated++;
                continue;
            }

            var saved = await Handle<UpdatePayrollProfileCommand, PayrollProfileDto>(command, cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
            {
                report.Failures.Add($"{p.Employee}: {saved.Error!.Message}");
                continue;
            }

            report.Created++;
        }

        if (cheque > 0)
        {
            report.Warnings.Add($"{cheque} employees are paid by cheque in ERPNext; they are set to cash, outside the bank transfer file.");
        }

        report.Check = $"{report.Created + report.AlreadyMigrated} of {considered} employees with payroll details in ERPNext have them in Gebeta";
        report.CheckPassed = report.Failures.Count == 0;
        return report;
    }
}
