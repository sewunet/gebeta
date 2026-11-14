using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;

namespace Gebeta.Api.Tests;

/// <summary>
/// Starts the real API against a real, throw-away PostgreSQL in Docker (same major version as
/// production), applies every module's migrations, and replaces Keycloak with a test sign-in:
/// the <c>X-Test-User</c> header names the signed-in user. "admin" is a bootstrap System Manager.
/// </summary>
public sealed class GebetaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminUser = "admin";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("gebeta_test")
        .WithUsername("gebeta")
        .WithPassword("gebeta_test")
        .Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    /// <summary>An HTTP client signed in as <paramref name="username"/>, or anonymous when null.</summary>
    public HttpClient ClientFor(string? username)
    {
        var client = CreateClient();
        if (username is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.Header, username);
        }

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Main", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Security:BootstrapAdmins:0", AdminUser);
        builder.UseSetting("RabbitMq:Enabled", "false"); // events stay in the outbox; delivery is tested in Gebeta.Messaging.Tests

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(o =>
            {
                o.DefaultScheme = TestAuthHandler.SchemeName;
                o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

/// <summary>Signs in the user named in the <c>X-Test-User</c> header, with Keycloak-style claims.</summary>
internal sealed class TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string Header = "X-Test-User";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Header, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var username = value.ToString();
        var identity = new ClaimsIdentity(
        [
            new Claim("sub", $"kc-{username}"),
            new Claim("preferred_username", username),
            new Claim("email", $"{username}@example.et"),
            new Claim("name", $"Test {username}"),
        ], SchemeName, "preferred_username", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
