using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Gebeta.Messaging;

/// <summary>Settings from the <c>RabbitMq</c> configuration section.</summary>
public sealed class RabbitMqOptions
{
    public const string Section = "RabbitMq";

    /// <summary>When false, events stay in the outboxes and nothing is sent or received (used by some tests).</summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "gebeta";
    public string Username { get; set; } = "gebeta";
    public string Password { get; set; } = string.Empty;
    public string Exchange { get; set; } = "gebeta.events";
    public string DeadLetterExchange { get; set; } = "gebeta.dlx";
    public ushort PrefetchCount { get; set; } = 10;

    /// <summary>Attempts per message before it goes to the module's dead-letter queue.</summary>
    public int MaxAttempts { get; set; } = 3;
}

/// <summary>One shared connection per process; channels are created per use.</summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private bool _topologyDeclared;

    public RabbitMqOptions Options { get; } = options.Value;

    public bool IsOpen => _connection?.IsOpen == true;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is { IsOpen: true } again)
            {
                return again;
            }

            var factory = new ConnectionFactory
            {
                HostName = Options.Host,
                Port = Options.Port,
                VirtualHost = Options.VirtualHost,
                UserName = Options.Username,
                Password = Options.Password,
                ClientProvidedName = $"gebeta:{Environment.MachineName}",
                AutomaticRecoveryEnabled = true,
            };
            _connection = await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            _topologyDeclared = false;
            return _connection;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Declares the events exchange (topic) and the dead-letter exchange (direct).</summary>
    public async Task EnsureTopologyAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (_topologyDeclared)
        {
            return;
        }

        await channel.ExchangeDeclareAsync(Options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        await channel.ExchangeDeclareAsync(Options.DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        _topologyDeclared = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _lock.Dispose();
    }
}

internal sealed class RabbitMqHealthCheck(RabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!connection.Options.Enabled)
        {
            return HealthCheckResult.Healthy("Messaging is disabled.");
        }

        try
        {
            await connection.GetAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
#pragma warning disable CA1031 // a health check reports any failure
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return HealthCheckResult.Unhealthy("RabbitMQ is not reachable.", ex);
        }
    }
}
