using System.Text;
using System.Text.Json;
using Gebeta.Persistence;
using Gebeta.Sample.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Gebeta.Messaging.Tests;

public class EventNamingTests
{
    [Theory]
    [InlineData(typeof(ThingHappened), "sample.thing-happened.v1")]
    [InlineData(typeof(PriceChangedV2), "sample.price-changed.v2")]
    public void Routing_keys_follow_the_module_event_version_convention(Type type, string expected)
    {
        Assert.Equal(expected, EventNaming.RoutingKeyFor(type));
    }

    [Fact]
    public void Events_outside_a_contracts_namespace_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => EventNaming.RoutingKeyFor(typeof(EventNamingTests)));
    }
}

/// <summary>
/// End to end against real RabbitMQ 4 and PostgreSQL 18: outbox → exchange → module queue → consumer
/// with inbox; repeated deliveries are skipped; failing messages go to the dead-letter queue.
/// </summary>
public sealed class MessagingTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18.3").Build();
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management").WithUsername("gebeta").WithPassword("gebeta").Build();
    private IHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(Ct), _rabbit.StartAsync(Ct));

        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = _rabbit.Hostname,
            ["RabbitMq:Port"] = _rabbit.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMq:VirtualHost"] = "/",
            ["RabbitMq:Username"] = "gebeta",
            ["RabbitMq:Password"] = "gebeta",
        });
        builder.Services.AddDbContext<SampleDbContext>(o => o.UseNpgsql(_postgres.GetConnectionString()));
        builder.Services.AddGebetaMessaging(builder.Configuration);
        builder.Services.AddOutboxDispatcher<SampleDbContext>("sample", SampleDbContext.Schema);
        builder.Services.AddIntegrationEventConsumer<ThingHappened, RecordThing, SampleDbContext>("sample");
        builder.Services.AddIntegrationEventConsumer<ThingBroke, AlwaysFails, SampleDbContext>("sample");
        _host = builder.Build();

        await using (var scope = _host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SampleDbContext>().Database.EnsureCreatedAsync(Ct);
        }

        await _host.StartAsync(Ct);
        await WaitForQueueAsync("gebeta.sample");
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync(CancellationToken.None);
        _host.Dispose();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbit.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task An_event_saved_in_the_outbox_reaches_the_subscribed_module()
    {
        var thingId = Guid.CreateVersion7();
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
            db.AddIntegrationEvent(new ThingHappened(thingId, "Raw sugar batch 42"), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(Ct);
        }

        await WaitUntilAsync(async db => await db.Received.CountAsync(r => r.ThingId == thingId, Ct) == 1);

        await using var check = _host.Services.CreateAsyncScope();
        var checkDb = check.ServiceProvider.GetRequiredService<SampleDbContext>();
        Assert.Equal("Raw sugar batch 42", (await checkDb.Received.SingleAsync(r => r.ThingId == thingId, Ct)).Name);
        Assert.All(await checkDb.Set<OutboxMessage>().Where(m => m.RoutingKey == "sample.thing-happened.v1").ToListAsync(Ct),
            m => Assert.NotNull(m.ProcessedAt));
    }

    [Fact]
    public async Task A_repeated_delivery_is_handled_only_once()
    {
        var thingId = Guid.CreateVersion7();
        var messageId = Guid.CreateVersion7();
        await PublishRawAsync("sample.thing-happened.v1", messageId, new ThingHappened(thingId, "Duplicate"));
        await PublishRawAsync("sample.thing-happened.v1", messageId, new ThingHappened(thingId, "Duplicate"));

        await WaitUntilAsync(async db => await db.Received.AnyAsync(r => r.ThingId == thingId, Ct));
        await WaitForEmptyQueueAsync("gebeta.sample");

        await using var scope = _host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SampleDbContext>();
        Assert.Equal(1, await db.Received.CountAsync(r => r.ThingId == thingId, Ct));
        Assert.Equal(1, await db.Set<InboxMessage>().CountAsync(m => m.MessageId == messageId, Ct));
    }

    [Fact]
    public async Task A_message_that_keeps_failing_goes_to_the_dead_letter_queue()
    {
        await PublishRawAsync("sample.thing-broke.v1", Guid.CreateVersion7(), new ThingBroke(Guid.CreateVersion7()));

        var deadLetters = 0u;
        for (var i = 0; i < 60 && deadLetters == 0; i++)
        {
            deadLetters = await MessageCountAsync("gebeta.sample.dlq");
            if (deadLetters == 0)
            {
                await Task.Delay(500, Ct);
            }
        }

        Assert.Equal(1u, deadLetters);
    }

    private async Task PublishRawAsync(string routingKey, Guid messageId, object body)
    {
        await using var connection = await NewConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        var properties = new BasicProperties { MessageId = messageId.ToString(), ContentType = "application/json", DeliveryMode = DeliveryModes.Persistent };
        await channel.BasicPublishAsync("gebeta.events", routingKey, mandatory: false, properties,
            Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body, body.GetType(), MessagingModel.Json)), Ct);
    }

    private async Task<uint> MessageCountAsync(string queue)
    {
        await using var connection = await NewConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(cancellationToken: Ct);
        return (await channel.QueueDeclarePassiveAsync(queue, Ct)).MessageCount;
    }

    private async Task WaitForQueueAsync(string queue)
    {
        for (var i = 0; i < 60; i++)
        {
            try
            {
                await MessageCountAsync(queue);
                return;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                await Task.Delay(500, Ct);
            }
        }

        throw new TimeoutException($"Queue {queue} was not declared.");
    }

    private async Task WaitForEmptyQueueAsync(string queue)
    {
        for (var i = 0; i < 40 && await MessageCountAsync(queue) > 0; i++)
        {
            await Task.Delay(250, Ct);
        }

        await Task.Delay(1000, Ct); // let an in-flight second delivery finish
    }

    private Task<IConnection> NewConnectionAsync() => new ConnectionFactory
    {
        HostName = _rabbit.Hostname,
        Port = _rabbit.GetMappedPublicPort(5672),
        UserName = "gebeta",
        Password = "gebeta",
    }.CreateConnectionAsync(Ct);

    private async Task WaitUntilAsync(Func<SampleDbContext, Task<bool>> condition)
    {
        for (var i = 0; i < 80; i++)
        {
            await using var scope = _host.Services.CreateAsyncScope();
            if (await condition(scope.ServiceProvider.GetRequiredService<SampleDbContext>()))
            {
                return;
            }

            await Task.Delay(250, Ct);
        }

        throw new TimeoutException("The event was not handled in time.");
    }
}
