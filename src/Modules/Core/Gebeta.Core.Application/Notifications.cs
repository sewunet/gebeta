using Gebeta.Application;
using Gebeta.Core.Application.Security;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Notifications;
using Gebeta.SharedKernel;

namespace Gebeta.Core.Application.Notifications;

public sealed record ListMyNotificationsQuery(bool UnreadOnly, int Page, int PageSize) : IQuery<NotificationPageDto>;

public sealed record CountMyUnreadQuery : IQuery<UnreadCountDto>;

public sealed record MarkNotificationReadCommand(Guid Id) : ICommand<NotificationDto>;

public sealed record MarkAllNotificationsReadCommand : ICommand<UnreadCountDto>;

/// <summary>
/// The signed-in user's own inbox (docs/requirements/sap-features.md #9). The user is always the one signed in, never taken from the request, and a
/// notification of someone else is treated as not found. Notifications of a company the user may no longer work in are not shown.
/// </summary>
public sealed class NotificationHandler(INotificationRepository notifications, UserContext user, ICompanyAccess access, Companies.ICoreUnitOfWork unitOfWork, TimeProvider clock)
    : IQueryHandler<ListMyNotificationsQuery, NotificationPageDto>, IQueryHandler<CountMyUnreadQuery, UnreadCountDto>,
      ICommandHandler<MarkNotificationReadCommand, NotificationDto>, ICommandHandler<MarkAllNotificationsReadCommand, UnreadCountDto>
{
    public const int MaxPageSize = 100;

    public async Task<Result<NotificationPageDto>> Handle(ListMyNotificationsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (await user.GetAsync(cancellationToken).ConfigureAwait(false) is not { } me)
        {
            return Error.NotFound("User", "current");
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var companies = await access.GetAccessibleCompanyIdsAsync(cancellationToken).ConfigureAwait(false);
        var found = await notifications.ListAsync(me.User.Id, companies, query.UnreadOnly, (page - 1) * size, size, cancellationToken).ConfigureAwait(false);
        var unread = await notifications.CountUnreadAsync(me.User.Id, companies, cancellationToken).ConfigureAwait(false);
        return new NotificationPageDto(found.Items.Select(ToDto).ToList(), found.Total, unread, page, size);
    }

    public async Task<Result<UnreadCountDto>> Handle(CountMyUnreadQuery query, CancellationToken cancellationToken)
    {
        if (await user.GetAsync(cancellationToken).ConfigureAwait(false) is not { } me)
        {
            return Error.NotFound("User", "current");
        }

        var companies = await access.GetAccessibleCompanyIdsAsync(cancellationToken).ConfigureAwait(false);
        return new UnreadCountDto(await notifications.CountUnreadAsync(me.User.Id, companies, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NotificationDto>> Handle(MarkNotificationReadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await user.GetAsync(cancellationToken).ConfigureAwait(false) is not { } me)
        {
            return Error.NotFound("User", "current");
        }

        // Someone else's notification is found as nothing, so its existence is not revealed.
        if (await notifications.GetOwnAsync(me.User.Id, command.Id, cancellationToken).ConfigureAwait(false) is not { } notification)
        {
            return Error.NotFound("Notification", command.Id);
        }

        notification.MarkRead(clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ToDto(notification);
    }

    public async Task<Result<UnreadCountDto>> Handle(MarkAllNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (await user.GetAsync(cancellationToken).ConfigureAwait(false) is not { } me)
        {
            return Error.NotFound("User", "current");
        }

        var companies = await access.GetAccessibleCompanyIdsAsync(cancellationToken).ConfigureAwait(false);
        await notifications.MarkAllReadAsync(me.User.Id, companies, clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        return new UnreadCountDto(await notifications.CountUnreadAsync(me.User.Id, companies, cancellationToken).ConfigureAwait(false));
    }

    private static NotificationDto ToDto(Notification n) => new(n.Id, n.CompanyId, n.Title, n.Body, n.LinkKind, n.LinkId, n.Severity, n.CreatedAt, n.ReadAt);
}
