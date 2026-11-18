using System.Net;
using System.Net.Http.Json;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>Users set up before their first sign-in (an administrator, or the data migration), linked when they first sign in.</summary>
public class UserInviteTests(GebetaApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Uri U(string path) => new(path, UriKind.Relative);

    [Fact]
    public async Task An_invited_user_gets_their_roles_on_first_sign_in_with_the_same_username_and_email()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var company = await (await admin.PostAsJsonAsync(U("/api/core/companies"),
            new { name = "Invite Test PLC", abbreviation = "UIT", legalForm = "PrivateLimitedCompany", tin = "0800000230" }, Ct)).Content.ReadFromJsonAsync<CompanyDto>(Ct);

        var invited = await admin.PostAsJsonAsync(U("/api/core/users"),
            new { username = "Lidya", email = "lidya@example.et", fullName = "Lidya Bekele", roles = new[] { "Accountant" }, companyIds = new[] { company!.Id } }, Ct);
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);
        var user = (await invited.Content.ReadFromJsonAsync<UserDto>(Ct))!;
        Assert.Equal(("lidya", true), (user.Username, user.IsInvited));
        Assert.Equal(["Accountant"], user.Roles);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(U("/api/core/users"), new { username = "lidya", email = "x@example.et" }, Ct)).StatusCode);

        // First sign-in: same username and email, so the invitation is hers.
        using var lidya = factory.ClientFor("lidya");
        var me = (await lidya.GetFromJsonAsync<MeDto>(U("/api/me"), Ct))!;
        Assert.Contains("Accountant", me.Roles);
        Assert.Equal([company.Id], me.CompanyIds);
        var listed = (await admin.GetFromJsonAsync<List<UserDto>>(U("/api/core/users"), Ct))!.Single(u => u.Username == "lidya");
        Assert.Equal((user.Id, false), (listed.Id, listed.IsInvited));
    }

    [Fact]
    public async Task Someone_with_the_invited_username_but_another_email_gets_nothing()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        var invited = await admin.PostAsJsonAsync(U("/api/core/users"),
            new { username = "tsion", email = "tsion@company.et", roles = new[] { "System Manager" } }, Ct);
        Assert.Equal(HttpStatusCode.OK, invited.StatusCode);

        using var impostor = factory.ClientFor("tsion"); // signs in as tsion@example.et
        Assert.False((await impostor.GetAsync(U("/api/me"), Ct)).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await impostor.GetAsync(U("/api/core/users"), Ct)).StatusCode);
        Assert.True((await admin.GetFromJsonAsync<List<UserDto>>(U("/api/core/users"), Ct))!.Single(u => u.Username == "tsion").IsInvited);
    }

    [Fact]
    public async Task A_deactivated_user_loses_access_and_nobody_can_deactivate_themselves()
    {
        using var admin = factory.ClientFor(GebetaApiFactory.AdminUser);
        using var kebede = factory.ClientFor("kebede.deactivate");
        Assert.Equal(HttpStatusCode.OK, (await kebede.GetAsync(U("/api/me"), Ct)).StatusCode); // first sign-in creates him

        var users = (await admin.GetFromJsonAsync<List<UserDto>>(U("/api/core/users"), Ct))!;
        var him = users.Single(u => u.Username == "kebede.deactivate");
        Assert.False((await (await admin.PostAsync(U($"/api/core/users/{him.Id}/deactivate"), null, Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!.IsActive);
        Assert.False((await kebede.GetAsync(U("/api/me"), Ct)).IsSuccessStatusCode);

        Assert.True((await (await admin.PostAsync(U($"/api/core/users/{him.Id}/activate"), null, Ct)).Content.ReadFromJsonAsync<UserDto>(Ct))!.IsActive);
        Assert.Equal(HttpStatusCode.OK, (await kebede.GetAsync(U("/api/me"), Ct)).StatusCode);

        var me = users.Single(u => u.Username == GebetaApiFactory.AdminUser);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync(U($"/api/core/users/{me.Id}/deactivate"), null, Ct)).StatusCode);
    }
}
