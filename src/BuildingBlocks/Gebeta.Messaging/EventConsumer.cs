using System.Text.Json;
using Gebeta.Application;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Gebeta.Messaging;

/// <summary>A module's subscription: which event, which handler, and which DbContext holds its inbox.</summary>
public sealed class EventSubscription
{
    internal EventSubscription(string moduleId, Type eventType, Type handlerType, Type dbContextType,
        Func<IServiceProvider, object, CancellationToken, Task> invoke)
    {
        ModuleId = moduleId;
        EventType = eventType;
        HandlerType = handlerType;
        DbContextType = dbContextType;
        RoutingKey = EventNaming.RoutingKeyFor(eventType);
        Invoke = invoke;
    }

    public string ModuleId { get; }
    public string RoutingKey { get; }
    public Type EventType { get; }
    public Type HandlerType { get; }
    public Type DbContextType { get; }
    internal Func<IServiceProvider, object, CancellationToken, Task> Invoke { get; }
}

/// <summary>
/// Receives events for every installed module: one quorum queue per module (<c>gebeta.&lt;module&gt;</c>)
/// bound only to the events that module subscribes to. Each handler runs in a transaction with the
/// module's inbox, so a repeated delivery is skipped. After <c>MaxAttempts</c> failures the message goes
/// to <c>gebeta.&lt;module&gt;.dlq</c> for an administrator to inspect and replay.
/// </summary>
internal sealed partial class EventConsumerService(
    IEnumerable<EventSubscription> subscriptions,
    IServiceScopeFactory scopes,
    RabbitMqConnection connection,
    TimeProvider clock,
    ILogger<EventConsumerService> logger) : BackgroundService
{
    private readonly List<IChannel> _channels = [];

    public static string QueueFor(string moduleId) => $"gebeta.{moduleId}";

    public static string DeadLetterQueueFor(string moduleId) => $"gebeta.{moduleId}.dlq";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = connection.Options;
        if (!options.Enabled)
        {
            return;
        }

        var byModule = subscriptions.GroupBy(s => s.ModuleId).ToList();
        if (byModule.Count == 0)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var conn = await connection.GetAsync(stoppingToken).ConfigureAwait(false);
                foreach (var module in byModule)
                {
                    await StartModuleConsumerAsync(conn, module.Key, module.ToList(), stoppingToken).ConfigureAwait(false);
                }

                return; // consumers keep running on their channels; automatic recovery reconnects them
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // broker not reachable yet: try again
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogStartFailed(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task StartModuleConsumerAsync(IConnection conn, string moduleId, List<EventSubscription> moduleSubscriptions, CancellationToken cancellationToken)
    {
        var options = connection.Options;
        var channel = await conn.CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        _channels.Add(channel);
        await connection.EnsureTopologyAsync(channel, cancellationToken).ConfigureAwait(false);

        var quorum = new Dictionary<string, object?> { ["x-queue-type"] = "quorum" };
        var deadLetterQueue = DeadLetterQueueFor(moduleId);
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, arguments: quorum, cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.QueueBindAsync(deadLetterQueue, options.DeadLetterExchange, moduleId, cancellationToken: cancellationToken).ConfigureAwait(false);

        var queue = QueueFor(moduleId);
        var arguments = new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = moduleId,
        };
        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, arguments: arguments, cancellationToken: cancellationToken).ConfigureAwait(false);
        foreach (var routingKey in moduleSubscriptions.Select(s => s.RoutingKey).Distinct())
        {
            await channel.QueueBindAsync(queue, options.Exchange, routingKey, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        await channel.BasicQosAsync(0, options.PrefetchCount, global: false, cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, moduleSubscriptions, delivery, cancellationToken);
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, cancellationToken).ConfigureAwait(false);
        LogConsuming(logger, moduleId, queue, moduleSubscriptions.Count);
    }

    private async Task OnMessageAsync(IChannel channel, List<EventSubscription> moduleSubscriptions, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        var body = delivery.Body.ToArray();
        var matching = moduleSubscriptions.Where(s => s.RoutingKey == delivery.RoutingKey).ToList();
        if (!Guid.TryParse(delivery.BasicProperties.MessageId, out var messageId) || matching.Count == 0)
        {
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken).ConfigureAwait(false);
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                foreach (var subscription in matching)
                {
                    await HandleAsync(subscription, messageId, body, cancellationToken).ConfigureAwait(false);
                }

                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken).ConfigureAwait(false);
                return;
            }
#pragma warning disable CA1031 // handler failures are retried, then dead-lettered
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogHandlerFailed(logger, ex, delivery.RoutingKey, messageId, attempt);
                if (attempt >= connection.Options.MaxAttempts)
                {
                    await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken).ConfigureAwait(false);
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleAsync(EventSubscription subscription, Guid messageId, byte[] body, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = (DbContext)scope.ServiceProvider.GetRequiredService(subscription.DbContextType);
        var handlerName = subscription.HandlerType.FullName ?? subscription.HandlerType.Name;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var alreadyHandled = await db.Set<InboxMessage>()
            .AnyAsync(m => m.MessageId == messageId && m.Handler == handlerName, cancellationToken).ConfigureAwait(false);
        if (alreadyHandled)
        {
            return;
        }

        var integrationEvent = JsonSerializer.Deserialize(body, subscription.EventType, MessagingModel.Json)
            ?? throw new InvalidOperationException($"Message {messageId} has an empty body.");
        await subscription.Invoke(scope.ServiceProvider, integrationEvent, cancellationToken).ConfigureAwait(false);

        db.Set<InboxMessage>().Add(new InboxMessage { MessageId = messageId, Handler = handlerName, ProcessedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        foreach (var channel in _channels)
        {
            await channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Module {ModuleId} consuming {Queue} ({Count} subscriptions)")]
    private static partial void LogConsuming(ILogger logger, string moduleId, string queue, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not start event consumers; retrying")]
    private static partial void LogStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handling {RoutingKey} message {MessageId} failed (attempt {Attempt})")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string routingKey, Guid messageId, int attempt);
}
