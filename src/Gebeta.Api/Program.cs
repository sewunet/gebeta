using Gebeta.Api;
using Gebeta.Core.Infrastructure;
using Gebeta.Modularity;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddHealthChecks();

// Every module the image contains. Which of them run is decided by Modules:Enabled.
builder.AddGebetaModules(
    new CoreModule());

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();

// Liveness: the process answers. Readiness: dependencies (database, RabbitMQ) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.MapGebetaModules();

app.Run();

/// <summary>Entry point, visible to integration tests.</summary>
public partial class Program;
