using System.Text;
using Gebeta.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Gebeta.Messaging;

/// <summary>
/// Sends one module's outbox to RabbitMQ: picks pending rows with <c>FOR UPDATE SKIP LOCKED</c> (so
/// several API replicas never send the same row twice), publishes with publisher confirms, and marks
/// them sent in the same transaction. A failed publish keeps the row for the next round.
/// </summary>
internal sealed partial class OutboxDispatcher<TDbContext>(
    string moduleId,
    string schema,
    IServiceScopeFactory scopes,
    RabbitMqConnection connection,
    TimeProvider clock,
    ILogger<OutboxDispatcher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private const int BatchSize = 50;
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(5);

    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!connection.Options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sent = await DispatchBatchAsync(stoppingToken).ConfigureAwait(false);
                if (sent < BatchSize)
                {
                    await Task.Delay(IdleDelay, clock, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // keep dispatching after transient failures (database or broker down)
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDispatchFailed(logger, ex, moduleId);
                await Task.Delay(ErrorDelay, clock, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    internal async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

#pragma warning disable EF1002 // schema comes from module code, not user input
        var batch = await db.Set<OutboxMessage>()
            .FromSqlRaw($"""SELECT * FROM "{schema}".{MessagingModel.OutboxTable} WHERE processed_at IS NULL ORDER BY occurred_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED""")
            .ToListAsync(cancellationToken).ConfigureAwait(false);
#pragma warning restore EF1002
        if (batch.Count == 0)
        {
            return 0;
        }

        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);
        var sent = 0;
        foreach (var message in batch)
        {
            try
            {
                var properties = new BasicProperties
                {
                    MessageId = message.Id.ToString(),
                    Type = message.RoutingKey,
                    ContentType = "application/json",
                    DeliveryMode = DeliveryModes.Persistent,
                    Timestamp = new AmqpTimestamp(message.OccurredAt.ToUnixTimeSeconds()),
                    AppId = moduleId,
                };
                await channel.BasicPublishAsync(connection.Options.Exchange, message.RoutingKey, mandatory: false, properties,
                    Encoding.UTF8.GetBytes(message.Payload), cancellationToken).ConfigureAwait(false);
                message.ProcessedAt = clock.GetUtcNow();
                sent++;
            }
#pragma warning disable CA1031 // record the failure on the row and retry later, keeping order
            catch (Exception ex)
#pragma warning restore CA1031
            {
                message.Attempts++;
                message.LastError = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                LogPublishFailed(logger, ex, message.Id, message.RoutingKey);
                break;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return sent;
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        var conn = await connection.GetAsync(cancellationToken).ConfigureAwait(false);
        _channel = await conn.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken).ConfigureAwait(false);
        await connection.EnsureTopologyAsync(_channel, cancellationToken).ConfigureAwait(false);
        return _channel;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        if (_channel is not null)
        {
            await _channel.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox dispatch for module {ModuleId} failed; retrying")]
    private static partial void LogDispatchFailed(ILogger logger, Exception exception, string moduleId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing outbox message {MessageId} ({RoutingKey}) failed")]
    private static partial void LogPublishFailed(ILogger logger, Exception exception, Guid messageId, string routingKey);
}
