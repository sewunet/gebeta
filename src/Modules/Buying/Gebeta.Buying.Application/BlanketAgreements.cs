using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Buying.Domain.Agreements;
using Gebeta.Buying.Domain.Orders;
using Gebeta.SharedKernel;

namespace Gebeta.Buying.Application;

public sealed record AgreementLineInput(Guid ItemId, decimal Rate, decimal? PlannedQty);

public sealed record CreateBlanketAgreementCommand(Guid CompanyId, Guid SupplierId, DateOnly AgreementDate, DateOnly ValidFrom, DateOnly ValidTo, decimal? AmountLimit,
    IReadOnlyList<AgreementLineInput> Lines, string? Remark) : ICommand<BlanketAgreementDto>;

public enum BlanketAgreementAction
{
    Submit,
    Cancel,
}

public sealed record BlanketAgreementActionCommand(Guid Id, BlanketAgreementAction Action) : ICommand<BlanketAgreementDto>;

public sealed record GetBlanketAgreementQuery(Guid Id) : IQuery<BlanketAgreementDto>;

public sealed record ListBlanketAgreementsQuery(Guid CompanyId, Guid? SupplierId = null) : IQuery<IReadOnlyList<BlanketAgreementDto>>;

/// <summary>Framework agreements: created, submitted, cancelled (only while no order has called off), and shown with what remains.</summary>
public sealed class BlanketAgreementHandler(
    IBlanketAgreementRepository agreements,
    IPurchaseOrderRepository orders,
    IItemDirectory items,
    IPartyDirectory parties,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IBuyingNumbers numbers,
    IBuyingUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreateBlanketAgreementCommand, BlanketAgreementDto>,
      ICommandHandler<BlanketAgreementActionCommand, BlanketAgreementDto>,
      IQueryHandler<GetBlanketAgreementQuery, BlanketAgreementDto>,
      IQueryHandler<ListBlanketAgreementsQuery, IReadOnlyList<BlanketAgreementDto>>
{
    public const string DefaultPattern = "BPA-.FY.-.#####";

    public async Task<Result<BlanketAgreementDto>> Handle(CreateBlanketAgreementCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var supplier = await parties.GetAsync(command.SupplierId, cancellationToken).ConfigureAwait(false);
        if (supplier is null || supplier.CompanyId != command.CompanyId || supplier.Type == "Customer")
        {
            return Error.Validation("Choose a supplier of this company.");
        }

        var catalogue = (await items.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(i => i.Id);
        var lines = new List<AgreementLine>();
        foreach (var l in command.Lines ?? [])
        {
            if (!catalogue.TryGetValue(l.ItemId, out var item) || !item.IsActive)
            {
                return Error.Validation("Every line needs an active item of this company.");
            }

            lines.Add(new AgreementLine(item.Id, item.Code, item.Name, item.Uom, l.Rate, l.PlannedQty));
        }

        BlanketAgreement agreement;
        try
        {
            agreement = BlanketAgreement.Create(command.CompanyId, supplier.Id, supplier.Name, command.AgreementDate, command.ValidFrom, command.ValidTo, command.AmountLimit, lines, command.Remark);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        agreement.AssignName(await numbers.NextAsync(command.CompanyId, agreement.DocumentType, DefaultPattern, command.AgreementDate, cancellationToken).ConfigureAwait(false));
        agreement.CreatedAt = clock.GetUtcNow();
        agreement.CreatedBy = currentUser.Username;
        agreements.Add(agreement);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([agreement], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<BlanketAgreementDto>> Handle(BlanketAgreementActionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Cancelling takes the agreement's lock, so an order cannot be submitted against it while it is being cancelled.
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var agreement = await agreements.GetLockedAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (agreement is null || !await access.CanAccessAsync(agreement.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Blanket agreement", command.Id);
        }

        try
        {
            if (command.Action == BlanketAgreementAction.Submit)
            {
                agreement.Submit();
            }
            else
            {
                var called = await orders.CalledOffAsync([agreement.Id], cancellationToken).ConfigureAwait(false);
                if (called.GetValueOrDefault(agreement.Id)?.Count > 0)
                {
                    return Error.Validation("Orders have called off against this agreement; cancel them first.");
                }

                agreement.Cancel();
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        agreement.ModifiedAt = clock.GetUtcNow();
        agreement.ModifiedBy = currentUser.Username;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([agreement], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<BlanketAgreementDto>> Handle(GetBlanketAgreementQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var agreement = await agreements.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return agreement is null || !await access.CanAccessAsync(agreement.CompanyId, cancellationToken).ConfigureAwait(false)
            ? Error.NotFound("Blanket agreement", query.Id)
            : (await ToDtosAsync([agreement], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<IReadOnlyList<BlanketAgreementDto>>> Handle(ListBlanketAgreementsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var list = await agreements.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false);
        if (query.SupplierId is { } supplier)
        {
            list = list.Where(a => a.SupplierId == supplier).ToList();
        }

        return Result.Success<IReadOnlyList<BlanketAgreementDto>>(await ToDtosAsync(list, cancellationToken).ConfigureAwait(false));
    }

    private async Task<List<BlanketAgreementDto>> ToDtosAsync(IReadOnlyList<BlanketAgreement> list, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Africa/Addis_Ababa")).DateTime);
        var called = await orders.CalledOffAsync(list.Where(a => a.DocStatus == DocStatus.Submitted).Select(a => a.Id).ToList(), cancellationToken).ConfigureAwait(false);
        return list.Select(a =>
        {
            var calls = called.GetValueOrDefault(a.Id) ?? [];
            var summary = BlanketAgreement.Summarise(calls);
            return new BlanketAgreementDto(a.Id, a.Name, a.CompanyId, a.SupplierId, a.SupplierName, a.PostingDate, a.ValidFrom, a.ValidTo, a.DocStatus.ToString(),
                a.StateFor(today, summary).ToString(), a.AmountLimit, summary.Sum(c => c.Amount), a.RemainingAmount(summary),
                a.Lines.Select(l => new AgreementLineDto(l.ItemId, l.ItemCode, l.ItemName, l.Uom, l.Rate, l.PlannedQty,
                    summary.Where(c => c.ItemId == l.ItemId).Sum(c => c.Qty), summary.Where(c => c.ItemId == l.ItemId).Sum(c => c.Amount), BlanketAgreement.RemainingQty(l, summary))).ToList(),
                calls.OrderBy(o => o.PostingDate).ThenBy(o => o.Name).Select(o => new AgreementCallOffDto(o.Id, o.Name, o.PostingDate, o.NetTotal, o.IsClosed ? "Closed" : "Submitted")).ToList(),
                a.Remark);
        }).ToList();
    }
}
