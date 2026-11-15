using Gebeta.Api;
using Gebeta.Core.Infrastructure;
using Gebeta.Messaging;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

// Sign-in is done by Keycloak (realm "gebeta"); the API only validates its access tokens.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        builder.Configuration.GetSection("Authentication").Bind(options);
        // Keycloak is reached over plain HTTP inside the Docker network; HTTPS is required elsewhere unless configured.
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Authentication:RequireHttpsMetadata", !builder.Environment.IsDevelopment());
        options.MapInboundClaims = false; // keep Keycloak's claim names: sub, preferred_username, email, name
        options.TokenValidationParameters.NameClaimType = "preferred_username";
    });
builder.Services.AddGebetaAuthorization();

// Integration events between modules go through RabbitMQ (docs/messaging.md).
builder.Services.AddGebetaMessaging(builder.Configuration);

// Every module the image contains. Which of them run is decided by Modules:Enabled.
builder.AddGebetaModules(
    new CoreModule(),
    new Gebeta.Payroll.Infrastructure.PayrollModule());

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi().AllowAnonymous();

// Liveness: the process answers. Readiness: dependencies (database, RabbitMQ) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status200OK } }).AllowAnonymous();

app.MapGebetaModules();

// "--migrate": update every module's schema and exit (the migrate container).
// Database:MigrateOnStartup: update, then keep running (development).
if (await app.RunMigrationsIfRequestedAsync(args).ConfigureAwait(false))
{
    return;
}

await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
