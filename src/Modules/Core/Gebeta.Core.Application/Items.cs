using Gebeta.Application;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Items;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Items;

public sealed record CreateItemCommand(Guid CompanyId, string Code, string Name, string Uom, string? Group, bool IsStockItem) : ICommand<ItemDto>;

public sealed record UpdateItemCommand(Guid Id, string Name, string Uom, string? Group, bool IsStockItem, bool IsActive) : ICommand<ItemDto>;

public sealed record ListItemsQuery(Guid CompanyId) : IQuery<IReadOnlyList<ItemDto>>;

public sealed record GetItemQuery(Guid Id) : IQuery<ItemDto>;

public sealed class ItemHandler(IItemRepository items, ICompanyAccess access, Companies.ICoreUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CreateItemCommand, ItemDto>, ICommandHandler<UpdateItemCommand, ItemDto>, IQueryHandler<ListItemsQuery, IReadOnlyList<ItemDto>>,
      IQueryHandler<GetItemQuery, ItemDto>, IItemDirectory
{
    public async Task<Result<ItemDto>> Handle(CreateItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!await access.CanAccessAsync(command.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", command.CompanyId);
        }

        var created = Item.Create(command.CompanyId, command.Code, command.Name, command.Uom, command.Group, command.IsStockItem);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        if (await items.CodeExistsAsync(command.CompanyId, created.Value.Code, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict($"Item {created.Value.Code} already exists.");
        }

        created.Value.CreatedAt = clock.GetUtcNow();
        items.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(created.Value);
    }

    public async Task<Result<ItemDto>> Handle(UpdateItemCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var item = await AccessibleAsync(command.Id, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return Error.NotFound("Item", command.Id);
        }

        var updated = item.Update(command.Name, command.Uom, command.Group, command.IsStockItem, command.IsActive);
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        item.ModifiedAt = clock.GetUtcNow();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(item);
    }

    public async Task<Result<IReadOnlyList<ItemDto>>> Handle(ListItemsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!await access.CanAccessAsync(query.CompanyId, cancellationToken).ConfigureAwait(false))
        {
            return Error.NotFound("Company", query.CompanyId);
        }

        return Result.Success<IReadOnlyList<ItemDto>>((await items.ListAsync(query.CompanyId, cancellationToken).ConfigureAwait(false)).Select(ToDto).ToList());
    }

    public async Task<Result<ItemDto>> Handle(GetItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var item = await AccessibleAsync(query.Id, cancellationToken).ConfigureAwait(false);
        return item is null ? Error.NotFound("Item", query.Id) : ToDto(item);
    }

    async Task<ItemInfo?> IItemDirectory.GetAsync(Guid itemId, CancellationToken cancellationToken) =>
        await items.GetAsync(itemId, cancellationToken).ConfigureAwait(false) is { } i ? ToInfo(i) : null;

    async Task<IReadOnlyList<ItemInfo>> IItemDirectory.ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await items.ListAsync(companyId, cancellationToken).ConfigureAwait(false)).Select(ToInfo).ToList();

    private async Task<Item?> AccessibleAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await items.GetAsync(id, cancellationToken).ConfigureAwait(false);
        return item is not null && await access.CanAccessAsync(item.CompanyId, cancellationToken).ConfigureAwait(false) ? item : null;
    }

    private static ItemInfo ToInfo(Item i) => new(i.Id, i.CompanyId, i.Code, i.Name, i.Uom, i.IsStockItem, i.IsActive);

    private static ItemDto ToDto(Item i) => new(i.Id, i.CompanyId, i.Code, i.Name, i.Uom, i.Group, i.IsStockItem, i.IsActive);
}
