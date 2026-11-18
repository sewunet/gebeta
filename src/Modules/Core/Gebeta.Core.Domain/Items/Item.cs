using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Items;

/// <summary>
/// A product or service the company buys, sells, stocks or uses (docs/modules/stock.md: Core keeps the item,
/// Stock adds its own settings such as the valuation method). Codes are unique per company.
/// </summary>
public sealed class Item : Entity, IAudited
{
    private Item()
    {
        Code = string.Empty;
        Name = string.Empty;
        Uom = string.Empty;
    }

    public Guid CompanyId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }

    /// <summary>Unit of measure stock is kept in, e.g. pcs, kg, quintal, litre.</summary>
    public string Uom { get; private set; }

    public string? Group { get; private set; }

    /// <summary>Stock items are counted in warehouses; services and other non-stock items are not.</summary>
    public bool IsStockItem { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static Result<Item> Create(Guid companyId, string code, string name, string uom, string? group, bool isStockItem)
    {
        var item = new Item { CompanyId = companyId };
        var set = item.Set(code, name, uom, group, isStockItem, true);
        return set.IsFailure ? set.Error! : item;
    }

    /// <summary>Changes the item; whether it is a stock item cannot change once it has stock movements (checked by the caller).</summary>
    public Result Update(string name, string uom, string? group, bool isStockItem, bool isActive) => Set(Code, name, uom, group, isStockItem, isActive);

    private Result Set(string code, string name, string uom, string? group, bool isStockItem, bool isActive)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length is < 1 or > 40 || code.Any(char.IsWhiteSpace))
        {
            return Result.Failure(Error.Validation("An item code is 1 to 40 characters without spaces."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 140)
        {
            return Result.Failure(Error.Validation("An item needs a name (at most 140 characters)."));
        }

        if (string.IsNullOrWhiteSpace(uom) || uom.Trim().Length > 20)
        {
            return Result.Failure(Error.Validation("An item needs a unit of measure (at most 20 characters)."));
        }

        (Code, Name, Uom, Group, IsStockItem, IsActive) = (code, name.Trim(), uom.Trim(), string.IsNullOrWhiteSpace(group) ? null : group.Trim(), isStockItem, isActive);
        return Result.Success();
    }
}

public interface IItemRepository
{
    Task<Item?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Item>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<bool> CodeExistsAsync(Guid companyId, string code, CancellationToken cancellationToken);
    void Add(Item item);
}
