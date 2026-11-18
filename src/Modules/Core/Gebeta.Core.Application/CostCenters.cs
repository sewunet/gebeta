using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.CostCenters;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.CostCenters;

public sealed record CreateCostCenterCommand(Guid CompanyId, string Code, string Name) : ICommand<CostCenterDto>;

public sealed record UpdateCostCenterCommand(Guid Id, string Name, bool IsActive) : ICommand<CostCenterDto>;

public sealed record ListCostCentersQuery(Guid CompanyId) : IQuery<IReadOnlyList<CostCenterDto>>;

/// <summary>Cost centres of a company (docs/requirements/sap-features.md #3); other modules read them through <see cref="ICostCenterDirectory"/>.</summary>
public sealed class CostCenterHandler(ICostCenterRepository costCenters, ICompanyAccess access, Companies.ICoreUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateCostCenterCommand, CostCenterDto>, ICommandHandler<UpdateCostCenterCommand, CostCenterDto>,
      IQueryHandler<ListCostCentersQuery, IReadOnlyList<CostCenterDto>>, ICostCenterDirectory
{
    public async Task<Result<CostCenterDto>> Handle(CreateCostCenterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var created = CostCenter.Create(command.CompanyId, command.Code, command.Name);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        if (await costCenters.CodeExistsAsync(command.CompanyId, created.Value.Code, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"Cost centre {created.Value.Code} already exists.");
        }

        created.Value.CreatedAt = clock.GetUtcNow();
        costCenters.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(created.Value);
    }

    public async Task<Result<CostCenterDto>> Handle(UpdateCostCenterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var center = await costCenters.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (center is null || !await access.CanAccessAsync(center.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Cost centre", command.Id);
        }

        var updated = center.Update(command.Name, command.IsActive);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        center.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(center);
    }

    public async Task<Result<IReadOnlyList<CostCenterDto>>> Handle(ListCostCentersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<CostCenterDto>>((await costCenters.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    async Task<CostCenterInfo?> ICostCenterDirectory.GetAsync(Guid costCenterId, CancellationToken cancellationToken) =>
        await costCenters.GetAsync(costCenterId, cancellationToken).ConfigureAwait(false) is { } c ? ToInfo(c) : null;

    async Task<IReadOnlyList<CostCenterInfo>> ICostCenterDirectory.ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await costCenters.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).Select(ToInfo).ToList();

    private static CostCenterInfo ToInfo(CostCenter c) => new(c.Id, c.CompanyId, c.Code, c.Name, c.IsMain, c.IsActive);

    private static CostCenterDto ToDto(CostCenter c) => new(c.Id, c.CompanyId, c.Code, c.Name, c.IsMain, c.IsActive);
}
