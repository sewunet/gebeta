using Gebeta.Application;
using Gebeta.Selling.Contracts;
using Gebeta.Selling.Domain.Orders;
using Gebeta.SharedKernel;

namespace Gebeta.Selling.Application;

public interface ISellingUnitOfWork : IUnitOfWork;

public interface ISellingNumbers : IDocumentNumbers;

/// <summary>Fulfilment of sales orders from the modules that hold it (Stock: delivered; Accounting: billed), or nothing when they are not installed.</summary>
public interface IOrderFulfilment
{
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> DeliveredAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<OrderedItemQty>>> BilledAsync(Guid companyId, IReadOnlyCollection<string> orders, CancellationToken cancellationToken);
}

public sealed record SalesOrderLineInput(Guid ItemId, decimal Qty, decimal Rate, Guid? WarehouseId);

public sealed record CreateSalesOrderCommand(Guid CompanyId, Guid CustomerId, DateOnly OrderDate, DateOnly? DeliveryDate, IReadOnlyList<SalesOrderLineInput> Lines, string? Remark)
    : ICommand<SalesOrderDto>;

public enum SalesOrderAction
{
    Submit,
    Cancel,
    Close,
    Reopen,
}

public sealed record SalesOrderActionCommand(Guid Id, SalesOrderAction Action) : ICommand<SalesOrderDto>;

public sealed record GetSalesOrderQuery(Guid Id) : IQuery<SalesOrderDto>;

public sealed record ListSalesOrdersQuery(Guid CompanyId, DateOnly From, DateOnly To) : IQuery<IReadOnlyList<SalesOrderDto>>;

public sealed class SalesOrderHandler(
    ISalesOrderRepository orders,
    IItemDirectory items,
    IPartyDirectory parties,
    IOrderFulfilment fulfilment,
    ICompanyAccess access,
    ICurrentUser currentUser,
    ISellingNumbers numbers,
    ISellingUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<CreateSalesOrderCommand, SalesOrderDto>,
      ICommandHandler<SalesOrderActionCommand, SalesOrderDto>,
      IQueryHandler<GetSalesOrderQuery, SalesOrderDto>,
      IQueryHandler<ListSalesOrdersQuery, IReadOnlyList<SalesOrderDto>>
{
    public const string DefaultPattern = "SO-.FY.-.#####";

    public async Task<Result<SalesOrderDto>> Handle(CreateSalesOrderCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var customer = await parties.GetAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer is null || customer.CompanyId != command.CompanyId || customer.Type == "Supplier")
        {
            return Error.Validation("Choose a customer of this company.");
        }

        var catalogue = (await items.ListAsync(command.CompanyId, cancellationToken).ConfigureAwait(false)).ToDictionary(i => i.Id);
        var lines = new List<SalesOrderLine>();
        foreach (var l in command.Lines ?? [])
        {
            if (!catalogue.TryGetValue(l.ItemId, out var item) || !item.IsActive)
            {
                return Error.Validation("Every line needs an active item of this company.");
            }

            lines.Add(new SalesOrderLine(item.Id, item.Code, item.Name, item.Uom, l.Qty, l.Rate, l.WarehouseId));
        }

        SalesOrder order;
        try
        {
            order = SalesOrder.Create(command.CompanyId, customer.Id, customer.Name, command.OrderDate, command.DeliveryDate ?? command.OrderDate, lines, command.Remark);
        }
        catch (DomainException ex)
        {
            return Error.Validation(ex.Message);
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

    public async Task<Result<SalesOrderDto>> Handle(SalesOrderActionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var order = await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (order is null)
        {
            return Error.NotFound("Sales order", command.Id);
        }

        try
        {
            switch (command.Action)
            {
                case SalesOrderAction.Submit: order.Submit(); break;
                case SalesOrderAction.Close: order.Close(); break;
                case SalesOrderAction.Reopen: order.Reopen(); break;
                default:
                    var delivered = await fulfilment.DeliveredAsync(order.CompanyId, [order.Name!], cancellationToken).ConfigureAwait(false);
                    var billed = await fulfilment.BilledAsync(order.CompanyId, [order.Name!], cancellationToken).ConfigureAwait(false);
                    if (delivered.GetValueOrDefault(order.Name!)?.Any(q => q.Qty != 0m) == true || billed.GetValueOrDefault(order.Name!)?.Any(q => q.Qty != 0m) == true)
                    {
                        return Error.Validation("Goods have been delivered or billed against this order; close it instead of cancelling.");
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
        return (await ToDtosAsync([order], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<SalesOrderDto>> Handle(GetSalesOrderQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var order = await AccessibleAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return order is null ? Error.NotFound("Sales order", query.Id) : (await ToDtosAsync([order], cancellationToken).ConfigureAwait(false))[0];
    }

    public async Task<Result<IReadOnlyList<SalesOrderDto>>> Handle(ListSalesOrdersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        var list = await orders.ListAsync(query.CompanyId, query.From, query.To, cancellationToken).ConfigureAwait(false);
        return Result.Success<IReadOnlyList<SalesOrderDto>>(await ToDtosAsync(list, cancellationToken).ConfigureAwait(false));
    }

    private async Task<SalesOrder?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var order = await orders.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return order is not null && await access.CanAccessAsync(order.CompanyId, cancellationToken).ConfigureAwait(false) ? order : null;
    }

    private async Task<List<SalesOrderDto>> ToDtosAsync(IReadOnlyList<SalesOrder> list, CancellationToken cancellationToken)
    {
        var live = list.Where(o => o.DocStatus != DocStatus.Draft && o.Name is not null).ToList();
        var delivered = new Dictionary<string, IReadOnlyList<OrderedItemQty>>();
        var billed = new Dictionary<string, IReadOnlyList<OrderedItemQty>>();
        foreach (var company in live.GroupBy(o => o.CompanyId))
        {
            var names = company.Select(o => o.Name!).ToList();
            foreach (var kv in await fulfilment.DeliveredAsync(company.Key, names, cancellationToken).ConfigureAwait(false))
            {
                delivered[kv.Key] = kv.Value;
            }

            foreach (var kv in await fulfilment.BilledAsync(company.Key, names, cancellationToken).ConfigureAwait(false))
            {
                billed[kv.Key] = kv.Value;
            }
        }

        static Dictionary<Guid, decimal> Sum(IReadOnlyList<OrderedItemQty>? q) =>
            (q ?? []).GroupBy(x => x.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Qty));
        return list.Select(o =>
        {
            var r = Sum(o.Name is null ? null : delivered.GetValueOrDefault(o.Name));
            var b = Sum(o.Name is null ? null : billed.GetValueOrDefault(o.Name));
            return new SalesOrderDto(o.Id, o.Name, o.CompanyId, o.CustomerId, o.CustomerName, o.PostingDate, o.DeliveryDate, o.DocStatus.ToString(), o.StateFor(r, b).ToString(),
                o.Lines.Select(l => new SalesOrderLineDto(l.ItemId, l.ItemCode, l.ItemName, l.Uom, l.Qty, l.Rate, l.Amount, l.WarehouseId,
                    r.GetValueOrDefault(l.ItemId), b.GetValueOrDefault(l.ItemId))).ToList(),
                o.NetTotal, o.PercentOf(r), o.PercentOf(b), o.Remark);
        }).ToList();
    }
}
