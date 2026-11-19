using System.Net;
using System.Net.Http.Json;
using Gebeta.Application;
using Gebeta.Buying.Contracts;
using Gebeta.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Gebeta.Api.Tests;

/// <summary>The in-app inbox (docs/requirements/sap-features.md #9): a user sees and changes only their own notifications.</summary>
public class NotificationTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    private Task<(HttpClient Client, MeDto Me, RoleDto Role)> UserWithAsync(HttpClient admin, string username, Guid? companyId, params string[] permissions) =>
        factory.UserWithAsync(admin, username, companyId, permissions);

    private static async Task<NotificationPageDto> InboxAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<NotificationPageDto>(U($"/api/notifications{query}"), Ct))!;

    private static async Task<int> UnreadAsync(HttpClient client) => (await client.GetFromJsonAsync<UnreadCountDto>(U("/api/notifications/unread-count"), Ct))!.Count;

    private async Task SendAsync(Guid companyId, string title, string? role = null, string? username = null, Guid? linkId = null)
    {
        using var scope = factory.Services.CreateScope();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
        var message = new NotificationMessage(title, "Body of " + title, linkId is null ? null : "PurchaseOrder", linkId, NotificationSeverity.Warning);
        if (role is not null)
        {
            await notifier.NotifyRoleAsync(companyId, role, message, Ct);
        }
        else
        {
            await notifier.NotifyUserAsync(companyId, username!, message, Ct);
        }
    }

    [Fact]
    public async Task Cancelling_an_order_notifies_its_creator_who_alone_can_read_and_mark_it()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Inbox Cancel PLC", Ct);
        var supplier = (await (await admin.PostAsJsonAsync(U("/api/core/parties"), new { companyId = company.Id, name = "Inbox Supplier", type = "Supplier" }, Ct)).Content.ReadFromJsonAsync<PartyDto>(Ct))!;
        var item = (await (await admin.PostAsJsonAsync(U("/api/core/items"), new { companyId = company.Id, code = "INB", name = "Inbox item", uom = "pc", isStockItem = true }, Ct)).Content.ReadFromJsonAsync<ItemDto>(Ct))!;

        var (maker, _, _) = await UserWithAsync(admin, "inbox.maker", company.Id, "buying.orders.read", "buying.orders.create", "buying.orders.submit");
        var (other, _, _) = await UserWithAsync(admin, "inbox.other", company.Id, "buying.orders.read");
        using var makerClient = maker;
        using var otherClient = other;

        var order = (await (await maker.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = company.Id, supplierId = supplier.Id, orderDate = "2026-11-10", lines = new[] { new { itemId = item.Id, qty = 2m, rate = 10m } },
        }, Ct)).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;

        // The creator submitting and cancelling their own order is not told about it.
        (await maker.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
        (await maker.PostAsync(U($"/api/buying/purchase-orders/{order.Id}/cancel"), null, Ct)).EnsureSuccessStatusCode();
        Assert.Empty((await InboxAsync(maker)).Items);

        // Someone else cancelling is: a second order, cancelled by the administrator.
        var second = (await (await maker.PostAsJsonAsync(U("/api/buying/purchase-orders"), new
        {
            companyId = company.Id, supplierId = supplier.Id, orderDate = "2026-11-11", lines = new[] { new { itemId = item.Id, qty = 1m, rate = 5m } },
        }, Ct)).Content.ReadFromJsonAsync<PurchaseOrderDto>(Ct))!;
        (await admin.PostAsync(U($"/api/buying/purchase-orders/{second.Id}/submit"), null, Ct)).EnsureSuccessStatusCode();
        (await admin.PostAsync(U($"/api/buying/purchase-orders/{second.Id}/cancel"), null, Ct)).EnsureSuccessStatusCode();

        var inbox = await InboxAsync(maker);
        var note = Assert.Single(inbox.Items);
        Assert.Equal((1, 1), (inbox.Total, inbox.Unread));
        Assert.Equal(("PurchaseOrder", second.Id, "Warning", company.Id, null), (note.LinkKind, note.LinkId, note.Severity, note.CompanyId, note.ReadAt));
        Assert.Contains(second.Number!, note.Title, StringComparison.Ordinal);
        Assert.Equal(1, await UnreadAsync(maker));

        // Another user sees none of it, cannot mark it read (found as nothing), and it stays unread for its owner.
        Assert.Empty((await InboxAsync(other)).Items);
        Assert.Equal(0, await UnreadAsync(other));
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync(U($"/api/notifications/{note.Id}/read"), null, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync(U($"/api/notifications/{note.Id}/read"), null, Ct)).StatusCode); // not even an administrator
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsync(U("/api/notifications/read-all"), null, Ct)).StatusCode);                 // marks only the caller's own
        Assert.Equal(1, await UnreadAsync(maker));

        // The owner marks it read; marking again keeps the first time.
        var read = (await (await maker.PostAsync(U($"/api/notifications/{note.Id}/read"), null, Ct)).Content.ReadFromJsonAsync<NotificationDto>(Ct))!;
        Assert.NotNull(read.ReadAt);
        var again = (await (await maker.PostAsync(U($"/api/notifications/{note.Id}/read"), null, Ct)).Content.ReadFromJsonAsync<NotificationDto>(Ct))!;
        Assert.Equal(read.ReadAt!.Value.ToUnixTimeMilliseconds(), again.ReadAt!.Value.ToUnixTimeMilliseconds()); // to the millisecond: the database keeps microseconds
        Assert.Equal(0, await UnreadAsync(maker));
        Assert.Empty((await InboxAsync(maker, "?unread=true")).Items);
        Assert.Single((await InboxAsync(maker)).Items);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.ClientFor(null).GetAsync(U("/api/notifications"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await maker.PostAsync(U($"/api/notifications/{Guid.NewGuid()}/read"), null, Ct)).StatusCode);
    }

    [Fact]
    public async Task The_inbox_pages_newest_first_filters_unread_and_marks_all_read()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Inbox Paging PLC", Ct);
        var (reader, _, _) = await UserWithAsync(admin, "inbox.pager", company.Id, "core.companies.read");
        using var readerClient = reader;

        foreach (var n in new[] { "first", "second", "third", "fourth", "fifth" })
        {
            await SendAsync(company.Id, n, username: "inbox.pager"); // sent one after the other, so created in this order
        }

        var page1 = await InboxAsync(reader, "?page=1&pageSize=2");
        Assert.Equal(["fifth", "fourth"], page1.Items.Select(i => i.Title));
        Assert.Equal((5, 5, 1, 2), (page1.Total, page1.Unread, page1.Page, page1.PageSize));
        Assert.Equal(["third", "second"], (await InboxAsync(reader, "?page=2&pageSize=2")).Items.Select(i => i.Title));
        Assert.Equal(["first"], (await InboxAsync(reader, "?page=3&pageSize=2")).Items.Select(i => i.Title));
        Assert.Equal(100, (await InboxAsync(reader, "?pageSize=100000")).PageSize); // paging is capped

        (await reader.PostAsync(U($"/api/notifications/{page1.Items[0].Id}/read"), null, Ct)).EnsureSuccessStatusCode();
        var unread = await InboxAsync(reader, "?unread=true");
        Assert.Equal(["fourth", "third", "second", "first"], unread.Items.Select(i => i.Title));
        Assert.Equal((4, 4), (unread.Total, unread.Unread));

        var all = (await (await reader.PostAsync(U("/api/notifications/read-all"), null, Ct)).Content.ReadFromJsonAsync<UnreadCountDto>(Ct))!;
        Assert.Equal(0, all.Count);
        Assert.Equal((5, 0, 0), ((await InboxAsync(reader)).Total, (await InboxAsync(reader)).Unread, (await InboxAsync(reader, "?unread=true")).Total));
    }

    [Fact]
    public async Task A_role_message_reaches_each_holder_who_may_work_in_the_company_and_nobody_else()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await admin.NewCompanyAsync("Inbox Role PLC", Ct);
        var elsewhere = await admin.NewCompanyAsync("Inbox Role Elsewhere PLC", Ct);
        var (inCompany, _, role) = await UserWithAsync(admin, "inbox.role.in", company.Id, "core.companies.read");
        var (outside, outsideMe, _) = await UserWithAsync(admin, "inbox.role.out", elsewhere.Id, "core.companies.read");
        using var inClient = inCompany;
        using var outClient = outside;
        // The second user holds the same role but works only in the other company.
        (await admin.PutAsync(U($"/api/core/users/{outsideMe.Id}/roles/{role.Id}"), null, Ct)).EnsureSuccessStatusCode();

        await SendAsync(company.Id, "Review the budget", role: role.Name);
        await SendAsync(company.Id, "Nobody holds this", role: "No such role");   // an unknown role or person is quietly ignored
        await SendAsync(company.Id, "Nobody home", username: "inbox.nobody");

        Assert.Equal(["Review the budget"], (await InboxAsync(inClient)).Items.Select(i => i.Title));
        Assert.Empty((await InboxAsync(outClient)).Items);

        // A user who may no longer work in a company does not see its notifications (access is checked when reading, too).
        (await admin.DeleteAsync(U($"/api/core/users/{(await inClient.GetFromJsonAsync<MeDto>(U("/api/me"), Ct))!.Id}/companies/{company.Id}"), Ct)).EnsureSuccessStatusCode();
        Assert.Empty((await InboxAsync(inClient)).Items);
        Assert.Equal(0, await UnreadAsync(inClient));
    }
}
