namespace Gebeta.Core.Contracts;

public sealed record ItemDto(Guid Id, Guid CompanyId, string Code, string Name, string Uom, string? Group, bool IsStockItem, bool IsActive);

/// <summary>A cost centre. The Main one takes every profit and loss posting that names no other.</summary>
public sealed record CostCenterDto(Guid Id, Guid CompanyId, string Code, string Name, bool IsMain, bool IsActive);

/// <summary>A file attached to a record.</summary>
public sealed record AttachmentDto(Guid Id, Guid CompanyId, Guid EntityId, string FileName, string ContentType, long Size, DateTimeOffset UploadedAt, string? UploadedBy);
