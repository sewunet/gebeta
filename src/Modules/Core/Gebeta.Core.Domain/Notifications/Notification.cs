using Gebeta.SharedKernel;

namespace Gebeta.Core.Domain.Notifications;

/// <summary>
/// One message in one user's inbox (docs/requirements/sap-features.md #9). It belongs to the user it was sent to: nobody else can read it or mark it
/// read. A message sent to a role is stored once for each user holding it, so reading is each person's own.
/// </summary>
public sealed class Notification : Entity
{
    public const int MaxTitle = 200;
    public const int MaxBody = 2000;
    public const int MaxKind = 60;

    private Notification()
    {
        Title = string.Empty;
        Severity = "Info";
    }

    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string Title { get; private set; }
    public string? Body { get; private set; }

    /// <summary>The kind and id of the record this is about, so the inbox can open it.</summary>
    public string? LinkKind { get; private set; }
    public Guid? LinkId { get; private set; }

    /// <summary>Info, Warning or Critical.</summary>
    public string Severity { get; private set; }

    public DateTimeOffset? ReadAt { get; private set; }

    public bool IsRead => ReadAt is not null;

    /// <summary>Too-long texts are cut rather than refused (a notification must never fail the action that sent it); a blank title is refused.</summary>
    public static Result<Notification> Create(Guid companyId, Guid userId, string title, string? body, string? linkKind, Guid? linkId, string severity, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return Error.Validation("A notification needs a title.");
        }

        var hasLink = !string.IsNullOrWhiteSpace(linkKind) && linkId is not null && linkId != Guid.Empty;
        return new Notification
        {
            CompanyId = companyId,
            UserId = userId,
            Title = Cut(title.Trim(), MaxTitle)!,
            Body = string.IsNullOrWhiteSpace(body) ? null : Cut(body.Trim(), MaxBody),
            LinkKind = hasLink ? Cut(linkKind!.Trim(), MaxKind) : null,
            LinkId = hasLink ? linkId : null,
            Severity = severity is "Warning" or "Critical" ? severity : "Info",
            CreatedAt = now,
        };
    }

    /// <summary>Marks it read; reading again keeps the first time.</summary>
    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    private static string? Cut(string? text, int max) => text is null || text.Length <= max ? text : text[..max];
}

public sealed record NotificationPage(IReadOnlyList<Notification> Items, int Total);

/// <summary>Every read and write is by the owner's user id: there is no way to reach another user's notification.</summary>
public interface INotificationRepository
{
    void Add(Notification notification);

    /// <summary>The notification with this id, only if it belongs to this user.</summary>
    Task<Notification?> GetOwnAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>Newest first; <paramref name="companyIds"/> null means every company. Total is the count before paging.</summary>
    Task<NotificationPage> ListAsync(Guid userId, IReadOnlySet<Guid>? companyIds, bool unreadOnly, int skip, int take, CancellationToken cancellationToken);

    Task<int> CountUnreadAsync(Guid userId, IReadOnlySet<Guid>? companyIds, CancellationToken cancellationToken);

    /// <summary>Marks all this user's unread notifications read in one statement; returns how many.</summary>
    Task<int> MarkAllReadAsync(Guid userId, IReadOnlySet<Guid>? companyIds, DateTimeOffset now, CancellationToken cancellationToken);
}
