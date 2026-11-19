using Gebeta.Application;
using Gebeta.Core.Application.Companies;
using Gebeta.Core.Contracts;
using Gebeta.Core.Domain.Notifications;
using Gebeta.Core.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Logging;

namespace Gebeta.Core.Infrastructure.Persistence;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notification");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Title).HasMaxLength(Notification.MaxTitle).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(Notification.MaxBody);
        builder.Property(n => n.LinkKind).HasMaxLength(Notification.MaxKind);
        builder.Property(n => n.Severity).HasMaxLength(10).IsRequired();
        builder.Property(n => n.CreatedBy).HasMaxLength(140);
        builder.Property(n => n.ModifiedBy).HasMaxLength(140);
        builder.Ignore(n => n.IsRead);
        builder.Ignore(n => n.DomainEvents);

        // The inbox is always read for one user, newest first; the unread count and the unread filter are the hot path (every page load).
        builder.HasIndex(n => new { n.UserId, n.CreatedAt }, "ix_notification_user_created").HasDatabaseName("ix_notification_user_created");
        builder.HasIndex(n => new { n.UserId, n.CreatedAt }, "ix_notification_user_created_unread").HasDatabaseName("ix_notification_user_created_unread").HasFilter("read_at IS NULL");
    }
}

internal sealed class NotificationRepository(CoreDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Set<Notification>().Add(notification);

    public Task<Notification?> GetOwnAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        db.Set<Notification>().FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken);

    public async Task<NotificationPage> ListAsync(Guid userId, IReadOnlySet<Guid>? companyIds, bool unreadOnly, int skip, int take, CancellationToken cancellationToken)
    {
        var query = Own(userId, companyIds);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(skip).Take(take).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new NotificationPage(items, total);
    }

    public Task<int> CountUnreadAsync(Guid userId, IReadOnlySet<Guid>? companyIds, CancellationToken cancellationToken) =>
        Own(userId, companyIds).CountAsync(n => n.ReadAt == null, cancellationToken);

    public Task<int> MarkAllReadAsync(Guid userId, IReadOnlySet<Guid>? companyIds, DateTimeOffset now, CancellationToken cancellationToken) =>
        Own(userId, companyIds).Where(n => n.ReadAt == null).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), cancellationToken);

    private IQueryable<Notification> Own(Guid userId, IReadOnlySet<Guid>? companyIds)
    {
        var query = db.Set<Notification>().AsNoTracking().Where(n => n.UserId == userId);
        if (companyIds is not null)
        {
            var allowed = companyIds.ToArray();
            query = query.Where(n => allowed.Contains(n.CompanyId));
        }

        return query;
    }
}

/// <summary>
/// Delivers notifications into the inbox (see <see cref="INotifier"/>). Best effort: any failure is logged and swallowed, so the business action
/// that sent the message is never lost to it. Nobody is notified who is unknown, inactive, or may not work in the company.
/// </summary>
internal sealed partial class CoreNotifier(IUserRepository users, IRoleRepository roles, INotificationRepository notifications, ICoreUnitOfWork unitOfWork, CoreDbContext db,
    TimeProvider clock, ILogger<CoreNotifier> logger) : INotifier
{
    public async Task NotifyUserAsync(Guid companyId, string username, NotificationMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        await SendAsync(companyId, message, async () =>
            await users.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false) is { } user ? [user] : [], cancellationToken).ConfigureAwait(false);
    }

    public async Task NotifyRoleAsync(Guid companyId, string roleName, NotificationMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return;
        }

        await SendAsync(companyId, message, async () =>
        {
            if (await roles.GetByNameAsync(roleName.Trim(), cancellationToken).ConfigureAwait(false) is not { } role)
            {
                return [];
            }

            // Users are few (hundreds at most), so the holders of a role are picked from the list.
            return (await users.ListAsync(cancellationToken).ConfigureAwait(false)).Where(u => u.RoleIds.Contains(role.Id)).ToList();
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync(Guid companyId, NotificationMessage message, Func<Task<IReadOnlyList<User>>> recipients, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var added = new List<Notification>();
        try
        {
            var now = clock.GetUtcNow();
            foreach (var user in await recipients().ConfigureAwait(false))
            {
                if (!user.IsActive || !await MayWorkInAsync(user, companyId, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var created = Notification.Create(companyId, user.Id, message.Title, message.Body, message.LinkKind, message.LinkId, message.Severity.ToString(), now);
                if (created.IsFailure)
                {
                    LogNotSent(created.Error!.Message);
                    return;
                }

                created.Value.CreatedBy = "system";
                notifications.Add(created.Value);
                added.Add(created.Value);
            }

            if (added.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex);
            foreach (var n in added)
            {
                db.Entry(n).State = EntityState.Detached; // do not leave a failed row to break a later save in this request
            }
        }
    }

    private async Task<bool> MayWorkInAsync(User user, Guid companyId, CancellationToken cancellationToken)
    {
        if (user.CompanyIds.Contains(companyId))
        {
            return true;
        }

        var held = await roles.GetManyAsync(user.RoleIds.ToList(), cancellationToken).ConfigureAwait(false);
        return held.Any(r => r.Allows(CorePermissions.CompaniesAccessAll));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Notification not sent: {Reason}")]
    private partial void LogNotSent(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A notification could not be delivered to the inbox.")]
    private partial void LogFailed(Exception ex);
}
