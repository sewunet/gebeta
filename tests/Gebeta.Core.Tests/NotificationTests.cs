using Gebeta.Core.Domain.Notifications;

namespace Gebeta.Core.Tests;

public class NotificationTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_notification_needs_a_title_keeps_its_link_only_when_complete_and_cuts_what_is_too_long()
    {
        var company = Guid.NewGuid();
        var user = Guid.NewGuid();
        Assert.True(Notification.Create(company, user, "  ", "body", null, null, "Info", At).IsFailure);

        var linked = Notification.Create(company, user, "  Order cancelled ", " why ", "PurchaseOrder", Guid.Parse("11111111-1111-1111-1111-111111111111"), "Warning", At).Value;
        Assert.Equal(("Order cancelled", "why", "PurchaseOrder", "Warning", false), (linked.Title, linked.Body, linked.LinkKind, linked.Severity, linked.IsRead));
        Assert.Equal((company, user, At), (linked.CompanyId, linked.UserId, linked.CreatedAt));

        // A kind without an id (or an id without a kind) is no link; an unknown severity is Info.
        var half = Notification.Create(company, user, "Hi", null, "PurchaseOrder", null, "Dramatic", At).Value;
        Assert.Equal((null, null, "Info", null), (half.LinkKind, half.LinkId, half.Severity, half.Body));

        var long_ = Notification.Create(company, user, new string('t', 500), new string('b', 5000), null, null, "Info", At).Value;
        Assert.Equal((Notification.MaxTitle, Notification.MaxBody), (long_.Title.Length, long_.Body!.Length));
    }

    [Fact]
    public void Reading_again_keeps_the_first_time_it_was_read()
    {
        var n = Notification.Create(Guid.NewGuid(), Guid.NewGuid(), "Hi", null, null, null, "Info", At).Value;
        n.MarkRead(At.AddHours(1));
        n.MarkRead(At.AddHours(5));
        Assert.True(n.IsRead);
        Assert.Equal(At.AddHours(1), n.ReadAt);
    }
}
