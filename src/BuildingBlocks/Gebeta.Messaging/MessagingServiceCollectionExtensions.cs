using Gebeta.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gebeta.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>RabbitMQ connection, event consumers and health check. Called once by the host.</summary>
    public static IServiceCollection AddGebetaMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Section));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<RabbitMqConnection>();
        services.AddHostedService<EventConsumerService>();
        services.AddHealthChecks().AddCheck<RabbitMqHealthCheck>("rabbitmq");
        return services;
    }

    /// <summary>Sends the outbox of the module whose DbContext is <typeparamref name="TDbContext"/> (schema <paramref name="schema"/>).</summary>
    public static IServiceCollection AddOutboxDispatcher<TDbContext>(this IServiceCollection services, string moduleId, string schema)
        where TDbContext : DbContext
    {
        services.AddSingleton<IHostedService>(sp => new OutboxDispatcher<TDbContext>(
            moduleId,
            schema,
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<RabbitMqConnection>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<OutboxDispatcher<TDbContext>>>()));
        return services;
    }

    /// <summary>
    /// Subscribes module <paramref name="moduleId"/> to <typeparamref name="TEvent"/>. The handler runs in a
    /// transaction with the module's inbox (<typeparamref name="TDbContext"/>). If the publishing module is not
    /// installed the subscription simply never receives anything.
    /// </summary>
    public static IServiceCollection AddIntegrationEventConsumer<TEvent, THandler, TDbContext>(this IServiceCollection services, string moduleId)
        where TEvent : class
        where THandler : class, IIntegrationEventConsumer<TEvent>
        where TDbContext : DbContext
    {
        services.AddScoped<THandler>();
        services.AddSingleton(new EventSubscription(moduleId, typeof(TEvent), typeof(THandler), typeof(TDbContext),
            (sp, integrationEvent, ct) => sp.GetRequiredService<THandler>().HandleAsync((TEvent)integrationEvent, ct)));
        return services;
    }
}
