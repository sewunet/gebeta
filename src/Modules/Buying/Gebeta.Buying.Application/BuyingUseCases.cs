using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Buying.Domain.Agreements;
using Gebeta.Buying.Domain.Orders;
using Gebeta.SharedKernel;

namespace Gebeta.Buying.Application;

public interface IBuyingUnitOfWork : IUnitOfWork;

public interface IBuyingNumbers : IDocumentNumbers;

/// <summary>Fulfilment of purchase orders from the modules that hold it (Stock: received; Accounting: billed), or nothing when they are not installed.</summary>
public interface IOrderFulfilment
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> ReceivedAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken);
}

public sealed record PurchaseOrderLineInput(Guid ItemId, decimal Qty, decimal Rate, Guid? WarehouseId);

public sealed record CreatePurchaseOrderCommand(Guid CompanyId, Guid SupplierId, DateOnly OrderDate, DateOnly? RequiredBy, IReadOnlyList<PurchaseOrderLineInput> Lines, string? Remark,
    Guid? AgreementId = null)
    : ICommand<PurchaseOrderDto>;

public enum PurchaseOrderAction
{
    Submit,
    Cancel,
    Close,
    Reopen,
}

public sealed record PurchaseOrderActionCommand(Guid Id, PurchaseOrderAction Action) : ICommand<PurchaseOrderDto>;

public sealed record GetPurchaseOrderQuery(Guid Id) : IQuery<PurchaseOrderDto>;

public sealed record ListPurchaseOrdersQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<PurchaseOrderDto>>;

public sealed class PurchaseOrderHandler(
    IPurchaseOrderRepository orders,
    IBlanketAgreementRepository agreements,
    IItemDirectory items,
    IPartyDirectory parties,
    IOrderFulfilment fulfilment,
    ICompanyAccess access,
    ICurrentUser currentUser,
    IBuyingNumbers numbers,
    IBuyingUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreatePurchaseOrderCommand, PurchaseOrderDto>,
      ICommandHandler<PurchaseOrderActionCommand, PurchaseOrderDto>,
      IQueryHandler<GetPurchaseOrderQuery, PurchaseOrderDto>,
      IQueryHandler<ListPurchaseOrdersQuery, IReadOnlyList<PurchaseOrderDto>>
{
    public const string DefaultPattern = "PO-.FY.-.#####";

    public async Task<Result<PurchaseOrderDto>> Handle(CreatePurchaseOrderCommand command, CancellationToken cancellationToken)
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
        var lines = new List<PurchaseOrderLine>();
        foreach (var l in command.Lines ?? [])
        {
            if (!catalogue.TryGetValue(l.ItemId, out var item) || !item.IsActive)
            {
                return Error.Validation("Every line needs an active item of this company.");
            }

            lines.Add(new PurchaseOrderLine(item.Id, item.Code, item.Name, item.Uom, l.Qty, l.Rate, l.WarehouseId));
        }

        PurchaseOrder order;
        try
        {
            order = PurchaseOrder.Create(command.CompanyId, supplier.Id, supplier.Name, command.OrderDate, command.RequiredBy ?? command.OrderDate, lines, command.Remark,
                command.AgreementId);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        if (command.AgreementId is { } agreementId)
        {
            // An early answer; submitting checks again under a lock.
            var agreement = await agreements.GetAsync(agreementId, cancellationToken).ConfigureAwait(false);
            if (agreement is null || agreement.CompanyId != command.CompanyId)
            {
                return Error.Validation("Choose an agreement of this company.");
            }

            if (await CheckCallOffAsync(agreement, order, cancellationToken).ConfigureAwait(false) is { } refusal)
            {
                return Error.Validation(refusal);
            }
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        order.AssignName(await numbers.NextAsync(command.CompanyId, order.DocumentType, DefaultPattern, command.OrderDate, cancellationToken).ConfigureAwait(false));
        order.CreatedAt = clock.GetUtcNow();
        order.CreatedBy = currentUser.Username;
        orders.Add(order);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([order], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<PurchaseOrderDto>> Handle(PurchaseOrderActionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var order = await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Purchase order", command.Id);
        }

        // Submitting an order that calls off against an agreement takes a lock on the agreement, so two orders cannot overdraw it together.
        await using var transaction = command.Action == PurchaseOrderAction.Submit && order.AgreementId is not null
            ? await unitOfWork.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;
        try
        {
            switch (command.Action)
            {
                case PurchaseOrderAction.Submit:
                    if (order.AgreementId is { } agreementId)
                    {
                        var agreement = await agreements.GetLockedAsync(agreementId, cancellationToken).ConfigureAwait(false);
                        if (agreement is null)
                        {
                            return Error.Validation("The agreement of this order no longer exists.");
                        }

                        if (await CheckCallOffAsync(agreement, order, cancellationToken).ConfigureAwait(false) is { } refusal)
                        {
                            return Error.Validation(refusal);
                        }
                    }

                    order.Submit();
                    break;
                case PurchaseOrderAction.Close: order.Close(); break;
                case PurchaseOrderAction.Reopen: order.Reopen(); break;
                default:
                    var received = await fulfilment.ReceivedAsync(order.CompanyId, [order.Name!], cancellationToken).ConfigureAwait(false);
                    var billed = await fulfilment.BilledAsync(order.CompanyId, [order.Name!], cancellationToken).ConfigureAwait(false);
                    if (received.GetValueOrDefault(order.Name!)?.Any(q => q.Qty != 0m) == true || billed.GetValueOrDefault(order.Name!)?.Any(q => q.Qty != 0m) == true)
                    {
                        return Error.Validation("Goods have been received or billed against this order; close it instead of cancelling.");
                    }

                    order.Cancel();
                    break;
            }
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
        }

        order.ModifiedAt = clock.GetUtcNow();
        order.ModifiedBy = currentUser.Username;
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        return (await ToDtosAsync([order], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<PurchaseOrderDto>> Handle(GetPurchaseOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var order = await AccessibleAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return order is null ? Error.NotFound("Purchase order", query.Id) : (await ToDtosAsync([order], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<IReadOnlyList<PurchaseOrderDto>>> Handle(ListPurchaseOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var list = await orders.ListAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<PurchaseOrderDto>>(await ToDtosAsync(list, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Why the order may not call off against the agreement as it stands, or null when it may.</summary>
    private async Task<string?> CheckCallOffAsync(BlanketAgreement agreement, PurchaseOrder order, CancellationToken cancellationToken)
    {
        var called = await orders.CalledOffAsync([agreement.Id], cancellationToken).ConfigureAwait(false);
        try
        {
            agreement.CheckCallOff(order.SupplierId, order.PostingDate, order.Lines, BlanketAgreement.Summarise(called.GetValueOrDefault(agreement.Id) ?? []));
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
    }

    private async Task<PurchaseOrder?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return order is not null && await access.CanAccessAsync(order.CompanyId, cancellationToken).ConfigureAwait(false) ? order : null;
    }

    private async Task<List<PurchaseOrderDto>> ToDtosAsync(IReadOnlyList<PurchaseOrder> list, CancellationToken cancellationToken)
    {
        var live = list.Where(o => o.DocStatus != DocStatus.Draft && o.Name is not null).ToList();
        var received = new Dictionary<string, IReadOnlyList<OrderedItemQty>>();
        var billed = new Dictionary<string, IReadOnlyList<OrderedItemQty>>();
        foreach (var company in live.GroupBy(o => o.CompanyId))
        {
            var names = company.Select(o => o.Name!).ToList();
            foreach (var kv in await fulfilment.ReceivedAsync(company.Key, names, cancellationToken).ConfigureAwait(false))
            {
                received[kv.Key] = kv.Value;
            }

            foreach (var kv in await fulfilment.BilledAsync(company.Key, names, cancellationToken).ConfigureAwait(false))
            {
                billed[kv.Key] = kv.Value;
            }
        }

        var agreementNumbers = (await agreements.GetManyAsync(list.Where(o => o.AgreementId is not null).Select(o => o.AgreementId!.Value).Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false)).ToDictionary(a => a.Id, a => a.Name);
        static Dictionary<Guid, decimal> Sum(IReadOnlyList<OrderedItemQty>? q) =>
            (q ?? []).GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        return list.Select(o =>
        {
            var r = Sum(o.Name is null ? null : received.GetValueOrDefault(o.Name));
            var b = Sum(o.Name is null ? null : billed.GetValueOrDefault(o.Name));
            return new PurchaseOrderDto(o.Id, o.Name, o.CompanyId, o.SupplierId, o.SupplierName, o.PostingDate, o.RequiredBy, o.DocStatus.ToString(), o.StateFor(r, b).ToString(),
                o.Lines.Select(l => new PurchaseOrderLineDto(l.ItemId, l.ItemCode, l.ItemName, l.Uom, l.Qty, l.Rate, l.Amount, l.WarehouseId,
                    r.GetValueOrDefault(l.ItemId), b.GetValueOrDefault(l.ItemId))).ToList(),
                o.NetTotal, o.PercentOf(r), o.PercentOf(b), o.Remark, o.AgreementId, o.AgreementId is { } a ? agreementNumbers.GetValueOrDefault(a) : null);
        }).ToList();
    }
}
