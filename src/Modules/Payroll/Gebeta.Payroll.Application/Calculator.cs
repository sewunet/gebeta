using Gebeta.Application;
using Gebeta.Payroll.Contracts;
using Gebeta.Payroll.Domain.Salary;
using Gebeta.Payroll.Domain.Tax;
using Gebeta.SharedKernel;

namespace Gebeta.Payroll.Application;

public sealed record CalculateSalaryQuery(CalculateSalaryRequest Request) : IQuery<SalaryCalculationDto>;

/// <summary>Calculates a salary with the tax table valid on the payroll date; nothing is stored.</summary>
public sealed class CalculateSalaryHandler : IQueryHandler<CalculateSalaryQuery, SalaryCalculationDto>
{
    public Task<Result<SalaryCalculationDto>> Handle(CalculateSalaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var request = query.Request;

        var lines = new List<SalaryComponentLine>();
        if (request.Components is null || request.Components.Count == 0)
        {
            lines.AddRange(EthiopianSalaryStructures.Standard);
        }
        else
        {
            foreach (var c in request.Components)
            {
                if (!Enum.TryParse<ComponentType>(c.Type, ignoreCase: true, out var type))
                {
                    return Task.FromResult<Result<SalaryCalculationDto>>(Error.Validation($"{c.Name}: type must be Earning, Deduction or EmployerContribution."));
                }

                lines.Add(new SalaryComponentLine(c.Name, c.Abbreviation, type, c.Formula, c.Amount, c.Condition, c.IsTaxable, c.DependsOnPaymentDays));
            }
        }

        var structure = SalaryStructure.Create("Calculator", lines, request.Variables?.Keys.ToList());
        if (structure.IsFailure)
        {
            return Task.FromResult<Result<SalaryCalculationDto>>(structure.Error!);
        }

        try
        {
            var table = EthiopianIncomeTax.ValidOn(request.PayrollDate);
            var slip = SalaryCalculator.Calculate(structure.Value,
                new SalaryInput(request.Base, request.PaymentDays, request.TotalWorkingDays, request.PayrollDate, request.Additional, request.Variables),
                table);

            return Task.FromResult<Result<SalaryCalculationDto>>(new SalaryCalculationDto(
                slip.Lines.Select(l => new SalaryLineDto(l.Component, l.Abbreviation, l.Type.ToString(), l.Amount, l.Formula)).ToList(),
                slip.GrossPay, slip.TaxableEarnings, slip.TotalDeductions, slip.NetPay, slip.EmployerContributions, slip.TaxTable));
        }
        catch (DomainException ex)
        {
            return Task.FromResult<Result<SalaryCalculationDto>>(Error.Validation(ex.Message));
        }
    }
}

public sealed record ListTaxTablesQuery : IQuery<IReadOnlyList<TaxTableDto>>;

public sealed class ListTaxTablesHandler : IQueryHandler<ListTaxTablesQuery, IReadOnlyList<TaxTableDto>>
{
    public Task<Result<IReadOnlyList<TaxTableDto>>> Handle(ListTaxTablesQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<TaxTableDto>>(EthiopianIncomeTax.All
            .Select(t => new TaxTableDto(t.Code, t.Name, t.ValidFrom, t.ValidTo, t.LegalSource,
                t.Slabs.Select(s => new TaxSlabDto(s.Over, s.UpTo, s.RatePercent, s.Deduction)).ToList()))
            .ToList()));
}
