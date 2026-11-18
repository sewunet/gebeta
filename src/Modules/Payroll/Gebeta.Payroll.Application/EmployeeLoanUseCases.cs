using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.BackPay;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Application;

/// <param name="Kind">Loan or StandingDeduction.</param>
/// <param name="StartMonth">A day in the first payroll period to deduct.</param>
/// <param name="EndMonth">A standing deduction's last period (optional).</param>
public sealed record CreateEmployeeLoanCommand(Guid CompanyId, Guid EmployeeId, string Kind, string Component, string? Abbreviation, decimal? Principal,
    decimal Instalment, DateOnly StartMonth, DateOnly? EndMonth, string? Remark) : ICommand<EmployeeLoanDto>;

public enum LoanAction
{
    Submit,
    Pause,
    Resume,
    Cancel,
}

public sealed record EmployeeLoanActionCommand(Guid Id, LoanAction Action) : ICommand<EmployeeLoanDto>;

/// <summary>Ends a loan early (the rest repaid another way) or stops a standing deduction after the period of <paramref name="Date"/>.</summary>
public sealed record SettleEmployeeLoanCommand(Guid Id, DateOnly Date, string? Reference) : ICommand<EmployeeLoanDto>;

public sealed record GetEmployeeLoanQuery(Guid Id) : IQuery<EmployeeLoanDto>;

public sealed record ListEmployeeLoansQuery(Guid CompanyId, Guid? EmployeeId) : IQuery<IReadOnlyList<EmployeeLoanDto>>;

/// <summary>Employee loans and standing deductions (docs/requirements/competitive-features.md #5). Salary slips take the instalments.</summary>
public sealed class EmployeeLoanHandler(
    IEmployeeLoanRepository loans,
    IAdditionalPayRepository entries,
    IEmployeeDirectory employees,
    ICompanyAccess access,
    IPayrollNumbers numbers,
    IPayrollUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreateEmployeeLoanCommand, EmployeeLoanDto>,
      ICommandHandler<EmployeeLoanActionCommand, EmployeeLoanDto>,
      ICommandHandler<SettleEmployeeLoanCommand, EmployeeLoanDto>,
      IQueryHandler<GetEmployeeLoanQuery, EmployeeLoanDto>,
      IQueryHandler<ListEmployeeLoansQuery, IReadOnlyList<EmployeeLoanDto>>
{
    public const string DefaultPattern = "LOAN-.FY.-.#####";

    public async Task<Result<EmployeeLoanDto>> Handle(CreateEmployeeLoanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        if (!Enum.TryParse<LoanKind>(command.Kind, ignoreCase: true, out var kind))
        {
            return Error.Validation("The kind is Loan or StandingDeduction.");
        }

        var employee = await employees.GetAsync(command.EmployeeId, cancellationToken).ConfigureAwait(false);
        if (employee is null || employee.CompanyId != command.CompanyId)
        {
            return Error.Validation("Choose an employee of this company.");
        }

        EmployeeLoan loan;
        try
        {
            loan = EmployeeLoan.Create(command.CompanyId, employee.Id, employee.Number, employee.FullName, kind, command.Component ?? string.Empty,
                command.Abbreviation, command.Principal, command.Instalment, command.StartMonth, command.EndMonth, command.Remark);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        loan.AssignName(await numbers.NextAsync(command.CompanyId, EmployeeLoan.Source, DefaultPattern, command.StartMonth, cancellationToken).ConfigureAwait(false));
        loan.CreatedAt = clock.GetUtcNow();
        loans.Add(loan);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(loan, []);
    }

    public async Task<Result<EmployeeLoanDto>> Handle(EmployeeLoanActionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } loan)
        {
            return Error.NotFound("Employee loan", command.Id);
        }

        var given = await entries.FromSourceAsync(loan.Id, cancellationToken).ConfigureAwait(false);
        try
        {
            switch (command.Action)
            {
                case LoanAction.Submit: loan.Submit(); break;
                case LoanAction.Pause: loan.Pause(); break;
                case LoanAction.Resume: loan.Resume(); break;
                case LoanAction.Cancel: loan.CancelWithEntries(given); break;
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        loan.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(loan, given);
    }

    public async Task<Result<EmployeeLoanDto>> Handle(SettleEmployeeLoanCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false) is not { } loan)
        {
            return Error.NotFound("Employee loan", command.Id);
        }

        try
        {
            loan.Settle(command.Date, command.Reference);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        loan.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(loan, await entries.FromSourceAsync(loan.Id, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<EmployeeLoanDto>> Handle(GetEmployeeLoanQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await AccessibleAsync(query.Id, cancellationToken).ConfigureAwait(false) is { } loan
            ? ToDto(loan, await entries.FromSourceAsync(loan.Id, cancellationToken).ConfigureAwait(false))
            : Error.NotFound("Employee loan", query.Id);
    }

    public async Task<Result<IReadOnlyList<EmployeeLoanDto>>> Handle(ListEmployeeLoansQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var list = await loans.ListAsync(query.CompanyId, query.EmployeeId, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<EmployeeLoanDto>>(list.Select(l => ToDto(l, null)).ToList());
    }

    private async Task<EmployeeLoan?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var loan = await loans.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return loan is not null && await access.CanAccessAsync(loan.CompanyId, cancellationToken).ConfigureAwait(false) ? loan : null;
    }

    public static string StatusOf(EmployeeLoan loan) => loan.DocStatus switch
    {
        DocStatus.Draft => "Draft",
        DocStatus.Cancelled => "Cancelled",
        _ when loan.IsCompleted => "Completed",
        _ when loan.IsPaused => "Paused",
        _ => "Running",
    };

    /// <param name="given">The loan's slip entries, to mark which instalments a slip holds (not loaded for lists).</param>
    private static EmployeeLoanDto ToDto(EmployeeLoan l, IReadOnlyList<AdditionalPayEntry>? given)
    {
        var inSlip = given?.Where(e => e.SalarySlipId is not null).Select(e => e.Id).ToHashSet() ?? [];
        return new(l.Id, l.Name, l.CompanyId, l.EmployeeId, l.EmployeeNumber, l.EmployeeName, l.Kind.ToString(), l.Component, l.Abbreviation, l.Principal,
            l.Instalment, l.StartMonth, l.EndMonth, StatusOf(l), l.Recovered, l.Balance, l.Remark,
            l.Repayments.Select(r => new LoanRepaymentDto(r.Date, r.Amount, r.Source, r.Reference, r.EntryId is { } id && inSlip.Contains(id))).ToList());
    }
}
