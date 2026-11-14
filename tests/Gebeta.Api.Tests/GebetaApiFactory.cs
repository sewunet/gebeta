using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Gebeta.Api.Tests;

/// <summary>
/// Starts the real API against a real, throw-away PostgreSQL in Docker (same major version as
/// production), and applies every module's migrations at start-up.
/// </summary>
public sealed class GebetaApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3")
        .WithDatabase("gebeta_test")
        .WithUsername("gebeta")
        .WithPassword("gebeta_test")
        .Build();

    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Main", _postgres.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}
