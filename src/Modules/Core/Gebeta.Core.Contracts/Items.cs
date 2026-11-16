namespace Gebeta.Core.Contracts;

public sealed record ItemDto(Guid Id, Guid CompanyId, string Code, string Name, string Uom, string? Group, bool IsStockItem, bool IsActive);
