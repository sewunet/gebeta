namespace Gebeta.Core.Contracts;

/// <summary>One message in the signed-in user's inbox. LinkKind and LinkId name the record it is about, if any.</summary>
public sealed record NotificationDto(Guid Id, Guid CompanyId, string Title, string? Body, string? LinkKind, Guid? LinkId, string Severity, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

/// <summary>A page of the inbox, newest first. Total counts every message matching the filter; Unread counts all unread ones.</summary>
public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, int Total, int Unread, int Page, int PageSize);

public sealed record UnreadCountDto(int Count);
