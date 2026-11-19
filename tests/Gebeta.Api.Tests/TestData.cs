using System.Net.Http.Json;
using Gebeta.Core.Contracts;

namespace Gebeta.Api.Tests;

/// <summary>
/// The API tests share one database, so company names, abbreviations and TINs must be unique across every test (three full runs went
/// red from clashes while each test passed alone). New tests take theirs from here instead of writing them by hand.
/// </summary>
public static class TestData
{
    private static int _next;

    private static int Next() => Interlocked.Increment(ref _next);

    /// <summary>Letters and digits; no hand-written test abbreviation has a digit, so these never clash with them.</summary>
    public static string Abbreviation() => $"T{Next():D4}";

    /// <summary>Ten digits starting 0098, a range no hand-written test TIN uses.</summary>
    public static string Tin() => $"0098{Next():D6}";

    /// <summary>A company of its own for one test.</summary>
    public static async Task<CompanyDto> NewCompanyAsync(this HttpClient admin, string name, CancellationToken cancellationToken, string legalForm = "PrivateLimitedCompany")
    {
        ArgumentNullException.ThrowIfNull(admin);
        var created = await admin.PostAsJsonAsync(new Uri("/api/core/companies", UriKind.Relative),
            new { name = $"{name} {Next()}", abbreviation = Abbreviation(), legalForm, tin = Tin() }, cancellationToken);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<CompanyDto>(cancellationToken))!;
    }

    /// <summary>
    /// A user signed in as <paramref name="username"/> (the first sign-in creates them) with a role of their own holding exactly these permissions, and, when
    /// <paramref name="companyId"/> is given, working in that company. Usernames must be unique across tests, like company names.
    /// </summary>
    public static async Task<(HttpClient Client, MeDto Me, RoleDto Role)> UserWithAsync(this GebetaApiFactory factory, HttpClient admin, string username, Guid? companyId,
        params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(admin);
        var ct = TestContext.Current.CancellationToken;
        var client = factory.ClientFor(username);
        var me = (await client.GetFromJsonAsync<MeDto>(new Uri("/api/me", UriKind.Relative), ct))!;
        var created = await admin.PostAsJsonAsync(new Uri("/api/core/roles", UriKind.Relative), new { name = $"Role of {username}", permissions }, ct);
        created.EnsureSuccessStatusCode();
        var role = (await created.Content.ReadFromJsonAsync<RoleDto>(ct))!;
        (await admin.PutAsync(new Uri($"/api/core/users/{me.Id}/roles/{role.Id}", UriKind.Relative), null, ct)).EnsureSuccessStatusCode();
        if (companyId is { } c)
        {
            (await admin.PutAsync(new Uri($"/api/core/users/{me.Id}/companies/{c}", UriKind.Relative), null, ct)).EnsureSuccessStatusCode();
        }

        return (client, me, role);
    }
}
