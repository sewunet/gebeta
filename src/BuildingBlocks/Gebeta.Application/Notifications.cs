namespace Gebeta.Application;

public enum NotificationSeverity
{
    Info,
    Warning,
    Critical,
}

/// <summary>
/// What a user finds in their inbox: a short title, an optional longer text, and optionally the record it is about (a kind such as
/// <c>PurchaseOrder</c>, the same names the document flow uses, and the record's id) so the inbox can link to it.
/// </summary>
public sealed record NotificationMessage(string Title, string? Body = null, string? LinkKind = null, Guid? LinkId = null, NotificationSeverity Severity = NotificationSeverity.Info);

/// <summary>
/// Sends a message to someone's in-app inbox (docs/requirements/sap-features.md #9). Provided by the Core module; any module may call it. Sending is
/// best effort and never throws for a missing person or a failure to store: a business action must not fail because a notification could not be
/// delivered. A person who is unknown, inactive or has no access to the company gets nothing (deny by default). A module that finds no notifier
/// uses <see cref="NoNotifier"/> and carries on.
/// </summary>
public interface INotifier
{
    /// <summary>To the user with this username, if they may work in the company.</summary>
    Task NotifyUserAsync(Guid companyId, string username, NotificationMessage message, CancellationToken cancellationToken);

    /// <summary>To every active user holding this role who may work in the company (one message each).</summary>
    Task NotifyRoleAsync(Guid companyId, string roleName, NotificationMessage message, CancellationToken cancellationToken);
}

/// <summary>The fallback when no notifier is installed: nothing is delivered and nothing fails.</summary>
public sealed class NoNotifier : INotifier
{
    public static NoNotifier Instance { get; } = new();

    public Task NotifyUserAsync(Guid companyId, string username, NotificationMessage message, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task NotifyRoleAsync(Guid companyId, string roleName, NotificationMessage message, CancellationToken cancellationToken) => Task.CompletedTask;
}
