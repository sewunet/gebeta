using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;
using Gebeta.SharedKernel.Ethiopia;

namespace Gebeta.Payroll.Application;

// ---------------------------------------------------------------- payroll profile

public sealed record UpdatePayrollProfileCommand(Guid EmployeeId, PaymentMethod PaymentMethod, string? BankName, string? BankAccountNumber, string? PensionNumber)
    : ICommand<PayrollProfileDto>;

public sealed class PayrollProfileHandler(IPayrollProfileRepository profiles, IEmployeeDirectory employees, ICompanyAccess access, IPayrollUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<UpdatePayrollProfileCommand, PayrollProfileDto>
{
    public async Task<Result<PayrollProfileDto>> Handle(UpdatePayrollProfileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || !await access.CanAccessAsync(employee.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Employee", command.EmployeeId);
        }

        var profile = await profiles.GetAsync(employee.Id, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            profile = EmployeePayrollProfile.Create(employee.CompanyId, employee.Id);
            profile.CreatedAt = clock.GetUtcNow();
            profiles.Add(profile);
        }

        var updated = profile.Update(command.PaymentMethod, command.BankName, command.BankAccountNumber, command.PensionNumber);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        profile.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new PayrollProfileDto(employee.Id, profile.PaymentMethod.ToString(), profile.BankName, profile.BankAccountNumber, profile.PensionNumber);
    }
}

// ---------------------------------------------------------------- payroll run

public sealed record CreatePayrollRunCommand(Guid CompanyId, DateOnly PeriodStart, DateOnly PeriodEnd) : ICommand<PayrollRunDto>;

public sealed record GeneratePayrollRunCommand(Guid RunId) : ICommand<PayrollRunDto>;

public sealed record SubmitPayrollRunCommand(Guid RunId) : ICommand<PayrollRunDto>;

public sealed record GetPayrollRunQuery(Guid RunId) : IQuery<PayrollRunDto>;

public sealed class PayrollRunHandler(
    IPayrollRunRepository runs,
    ISalarySlipRepository slips,
    IEmployeeList employeeList,
    ICompanyDirectory companies,
    ICompanyAccess access,
    SalarySlipHandler slipHandler,
    IPayrollNumbers numbers,
    IPayrollUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreatePayrollRunCommand, PayrollRunDto>,
      ICommandHandler<GeneratePayrollRunCommand, PayrollRunDto>,
      ICommandHandler<SubmitPayrollRunCommand, PayrollRunDto>,
      IQueryHandler<GetPayrollRunQuery, PayrollRunDto>
{
    public const string DefaultPattern = "PR-.FY.-.####";

    public async Task<Result<PayrollRunDto>> Handle(CreatePayrollRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)
            || await companies.GetAsync(command.CompanyId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        PayrollRun run;
        try
        {
            run = PayrollRun.Create(command.CompanyId, command.PeriodStart, command.PeriodEnd);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        run.AssignName(await numbers.NextAsync(command.CompanyId, run.DocumentType, DefaultPattern, command.PeriodEnd, cancellationToken).ConfigureAwait(false));
        run.CreatedAt = clock.GetUtcNow();
        runs.Add(run);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(run, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Creates a draft slip for each employee employed in the period; employees that cannot be paid are listed as issues.</summary>
    public async Task<Result<PayrollRunDto>> Handle(GeneratePayrollRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var run = await AccessibleRunAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return Error.NotFound("Payroll run", command.RunId);
        }

        if (run.DocStatus != DocStatus.Draft || run.SlipsGenerated)
        {
            return Error.Conflict("The slips of this payroll run were already generated.");
        }

        var slipIds = new List<Guid>();
        var issues = new List<PayrollRunIssue>();
        foreach (var employee in await employeeList.ListAsync(run.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            if (employee.DateOfJoining > run.PeriodEnd || employee.DateOfLeaving < run.PeriodStart)
            {
                continue; // not employed in this month: not an issue
            }

            var result = await slipHandler.Handle(new CreateSalarySlipCommand(employee.Id, run.PeriodStart, run.PeriodEnd), cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                slipIds.Add(result.Value.Id);
            }
            else
            {
                issues.Add(new PayrollRunIssue(employee.Id, employee.FullName, result.Error!.Message));
            }
        }

        run.RecordGenerated(slipIds, issues);
        run.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(run, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PayrollRunDto>> Handle(SubmitPayrollRunCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var run = await AccessibleRunAsync(command.RunId, cancellationToken).ConfigureAwait(false);
        if (run is null)
        {
            return Error.NotFound("Payroll run", command.RunId);
        }

        var failures = new List<string>();
        foreach (var slipId in run.SlipIds)
        {
            var slip = await slips.GetAsync(slipId, cancellationToken).ConfigureAwait(false);
            if (slip is null || slip.DocStatus != DocStatus.Draft)
            {
                continue;
            }

            var submitted = await slipHandler.Handle(new SubmitSalarySlipCommand(slipId), cancellationToken).ConfigureAwait(false);
            if (submitted.IsFailure)
            {
                failures.Add($"{slip.EmployeeName}: {submitted.Error!.Message}");
            }
        }

        if (failures.Count > 0)
        {
            return Error.Validation($"Some slips could not be submitted: {string.Join("; ", failures)}");
        }

        try
        {
            run.Submit();
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        run.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(run, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<PayrollRunDto>> Handle(GetPayrollRunQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var run = await AccessibleRunAsync(query.RunId, cancellationToken).ConfigureAwait(false);
        return run is null ? Error.NotFound("Payroll run", query.RunId) : await ToDtoAsync(run, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PayrollRun?> AccessibleRunAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await runs.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return run is not null && await access.CanAccessAsync(run.CompanyId, cancellationToken).ConfigureAwait(false) ? run : null;
    }

    private async Task<PayrollRunDto> ToDtoAsync(PayrollRun run, CancellationToken cancellationToken)
    {
        var ids = run.SlipIds.ToHashSet();
        var runSlips = ids.Count == 0
            ? []
            : (await slips.ListAsync(run.CompanyId, run.PeriodStart, run.PeriodEnd, cancellationToken).ConfigureAwait(false)).Where(s => ids.Contains(s.Id)).ToList();
        return new PayrollRunDto(run.Id, run.Name, run.CompanyId, run.PeriodStart, run.PeriodEnd, run.DocStatus.ToString(), run.SlipsGenerated,
            run.SlipIds.Count, runSlips.Sum(s => s.GrossPay), runSlips.Sum(s => s.NetPay),
            run.Issues.Select(i => new PayrollRunIssueDto(i.EmployeeId, i.EmployeeName, i.Reason)).ToList());
    }
}

// ---------------------------------------------------------------- statutory and bank reports

public sealed record IncomeTaxDeclarationQuery(Guid CompanyId, DateOnly PeriodStart, DateOnly PeriodEnd) : IQuery<IncomeTaxDeclarationDto>;

public sealed record PensionScheduleQuery(Guid CompanyId, DateOnly PeriodStart, DateOnly PeriodEnd) : IQuery<PensionScheduleDto>;

public sealed record BankTransferQuery(Guid CompanyId, DateOnly PeriodStart, DateOnly PeriodEnd) : IQuery<BankTransferDto>;

/// <summary>
/// Monthly reports from submitted slips: the employment income tax declaration, the pension contribution
/// schedule and the bank transfer list. Back payment tax and pension (BP_TAX, BP_DED, BP_EMP) are included
/// in the month they are paid. Column layouts follow the usual Ethiopian forms; VERIFY against the current
/// Ministry of Revenue and pension agency templates before filing.
/// </summary>
public sealed class PayrollReportsHandler(
    ISalarySlipRepository slips,
    IPayrollProfileRepository profiles,
    IEmployeeList employeeList,
    ICompanyDirectory companies,
    ICompanyAccess access,
    PayrollOptions options,
    SlipFigures figures)
    : IQueryHandler<IncomeTaxDeclarationQuery, IncomeTaxDeclarationDto>,
      IQueryHandler<PensionScheduleQuery, PensionScheduleDto>,
      IQueryHandler<BankTransferQuery, BankTransferDto>
{
    public async Task<Result<IncomeTaxDeclarationDto>> Handle(IncomeTaxDeclarationQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var data = await LoadAsync(query.CompanyId, query.PeriodStart, query.PeriodEnd, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var (company, submitted, employees, _) = data.Value;
        var rows = submitted.Select(s =>
        {
            var tin = employees.GetValueOrDefault(s.EmployeeId)?.Tin;
            var joined = employees.GetValueOrDefault(s.EmployeeId)?.DateOfJoining ?? s.PeriodStart;
            var earnings = s.Lines.Where(l => l.Type == ComponentType.Earning).Sum(l => l.Amount);
            return new IncomeTaxRow(s.EmployeeNumber, s.EmployeeName, tin, joined, s.AmountOf(options.BasicComponent), s.TaxableEarnings,
                earnings - s.TaxableEarnings, Sum(s, IsIncomeTax), Sum(s, IsEmployeePension), s.NetPay);
        }).ToList();

        return new IncomeTaxDeclarationDto(company.Name, company.Tin, query.PeriodStart, query.PeriodEnd, EthiopianMonth(query.PeriodStart),
            rows, rows.Sum(r => r.TaxableEarnings), rows.Sum(r => r.TaxWithheld), rows.Count);
    }

    public async Task<Result<PensionScheduleDto>> Handle(PensionScheduleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var data = await LoadAsync(query.CompanyId, query.PeriodStart, query.PeriodEnd, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var (company, submitted, employees, profileMap) = data.Value;
        var rows = submitted.Select(s =>
        {
            var employee = Sum(s, IsEmployeePension);
            var employer = Sum(s, IsEmployerPension);
            return new PensionRow(s.EmployeeNumber, s.EmployeeName, profileMap.GetValueOrDefault(s.EmployeeId)?.PensionNumber,
                employees.GetValueOrDefault(s.EmployeeId)?.Tin, s.AmountOf(options.BasicComponent), employee, employer, employee + employer);
        }).ToList();

        return new PensionScheduleDto(company.Name, company.Tin, query.PeriodStart, query.PeriodEnd, EthiopianMonth(query.PeriodStart), rows,
            rows.Sum(r => r.EmployeeContribution), rows.Sum(r => r.EmployerContribution), rows.Sum(r => r.Total));
    }

    public async Task<Result<BankTransferDto>> Handle(BankTransferQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var data = await LoadAsync(query.CompanyId, query.PeriodStart, query.PeriodEnd, cancellationToken).ConfigureAwait(false);
        if (data is null)
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var (company, submitted, _, profileMap) = data.Value;
        var bank = new List<BankTransferRow>();
        var cash = new List<BankTransferRow>();
        var issues = new List<string>();
        foreach (var s in submitted)
        {
            var profile = profileMap.GetValueOrDefault(s.EmployeeId);
            var row = new BankTransferRow(s.EmployeeNumber, s.EmployeeName, profile?.BankName, profile?.BankAccountNumber, s.NetPay);
            if (profile is null)
            {
                issues.Add($"{s.EmployeeName} ({s.EmployeeNumber}) has no payroll profile; paid in cash until a bank account is added.");
                cash.Add(row);
            }
            else if (profile.PaymentMethod == PaymentMethod.Cash)
            {
                cash.Add(row);
            }
            else
            {
                bank.Add(row);
            }
        }

        return new BankTransferDto(company.Name, query.PeriodStart, query.PeriodEnd, bank, cash, issues, bank.Sum(r => r.NetPay), cash.Sum(r => r.NetPay));
    }

    private async Task<(CompanyInfo Company, List<SalarySlip> Slips, Dictionary<Guid, EmployeeInfo> Employees, Dictionary<Guid, EmployeePayrollProfile> Profiles)?>
        LoadAsync(Guid companyId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (!await access.CanAccessAsync(companyId, cancellationToken).ConfigureAwait(false)
            || await companies.GetAsync(companyId, cancellationToken).ConfigureAwait(false) is not { } company)
        {
            return null;
        }

        var submitted = (await slips.ListAsync(companyId, from, to, cancellationToken).ConfigureAwait(false))
            .Where(s => s.DocStatus == DocStatus.Submitted).OrderBy(s => s.EmployeeNumber, StringComparer.Ordinal).ToList();
        var employees = (await employeeList.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).ToDictionary(e => e.Id);
        var profileMap = (await profiles.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.EmployeeId);
        return (company, submitted, employees, profileMap);
    }

    private static decimal Sum(SalarySlip slip, Func<SalarySlipLine, bool> predicate) => slip.Lines.Where(predicate).Sum(l => l.Amount);

    private bool IsIncomeTax(SalarySlipLine line) => figures.IsIncomeTax(line);

    private bool IsEmployeePension(SalarySlipLine line) => figures.IsEmployeePension(line);

    private bool IsEmployerPension(SalarySlipLine line) => figures.IsEmployerPension(line);

    private static string EthiopianMonth(DateOnly date)
    {
        var e = EthiopianDate.FromGregorian(date);
        return $"{EthiopianMonthNames.English(e.MonthName)} {e.Year}";
    }
}

/// <summary>
/// Which slip lines are income tax, employee pension and employer pension: one set of rules for the statutory reports and the payroll
/// comparison, so their figures agree. Back payment lines (BP_TAX, BP_DED, BP_EMP) count in the month they are paid.
/// </summary>
public sealed class SlipFigures(PayrollOptions options)
{
    public bool IsIncomeTax(SalarySlipLine line) =>
        line.Type == ComponentType.Deduction && (options.IncomeTaxComponents.Contains(line.Abbreviation) || line.Abbreviation == "BP_TAX"
            || (line.Formula?.Contains("income_tax(", StringComparison.Ordinal) ?? false));

    public bool IsEmployeePension(SalarySlipLine line) =>
        line.Type == ComponentType.Deduction && (options.EmployeePensionComponents.Contains(line.Abbreviation) || line.Abbreviation == "BP_DED");

    public bool IsEmployerPension(SalarySlipLine line) =>
        line.Type == ComponentType.EmployerContribution && (options.EmployerPensionComponents.Contains(line.Abbreviation) || line.Abbreviation == "BP_EMP");

    public decimal IncomeTax(SalarySlip slip) => slip.Lines.Where(IsIncomeTax).Sum(l => l.Amount);

    public decimal EmployeePension(SalarySlip slip) => slip.Lines.Where(IsEmployeePension).Sum(l => l.Amount);

    public decimal EmployerPension(SalarySlip slip) => slip.Lines.Where(IsEmployerPension).Sum(l => l.Amount);
}

