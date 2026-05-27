using System.Text;
using Nop.Core.Configuration;
using Nop.Services.Logging;
using RabbitMQ.Client;

namespace Nop.Services.Integration.Messaging;

/// <summary>
/// RabbitMQ.Client-based publisher.
///
/// Singleton scope: the underlying <see cref="IConnection"/> is expensive
/// to open and is designed to be long-lived. A fresh channel is opened
/// per publish — channels are cheap and not thread-safe, so this avoids
/// cross-thread serialization issues at the cost of one channel-open per
/// message. Publishing happens against a topic exchange named by
/// <see cref="RabbitMqConfig.ExchangeName"/>.
///
/// Phase 1 behaviour: if the broker is unreachable, the publisher logs
/// and swallows the error — no caller is blocked. The connection is
/// re-attempted on the next publish so transient broker unavailability
/// self-heals. Phase 2 introduces the outbox that turns this into
/// durable, retried delivery.
/// </summary>
public partial class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
{
    #region Fields

    protected readonly RabbitMqConfig _config;
    protected readonly ILogger _logger;
    protected readonly object _connectionLock = new();
    protected IConnection _connection;
    protected bool _disposed;

    #endregion

    #region Ctor

    public RabbitMqPublisher(RabbitMqConfig config, ILogger logger)
    {
        _config = config;
        _logger = logger;
    }

    #endregion

    #region Utilities

    protected virtual IConnection EnsureConnection()
    {
        if (_connection is { IsOpen: true })
            return _connection;

        lock (_connectionLock)
        {
            if (_connection is { IsOpen: true })
                return _connection;

            _connection?.Dispose();

            var factory = new ConnectionFactory
            {
                HostName = _config.Host,
                Port = _config.Port,
                UserName = _config.Username,
                Password = _config.Password,
                VirtualHost = _config.VirtualHost,
                DispatchConsumersAsync = true,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection("verdemart-nopcommerce");

            using var channel = _connection.CreateModel();
            channel.ExchangeDeclare(
                exchange: _config.ExchangeName,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);

            return _connection;
        }
    }

    #endregion

    #region Methods

    public virtual Task PublishAsync(string routingKey, string payload, Guid eventId, CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
            return Task.CompletedTask;

        try
        {
            var connection = EnsureConnection();
            using var channel = connection.CreateModel();

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.ContentType = "application/json";
            properties.MessageId = eventId.ToString("D");
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            properties.Type = routingKey;

            var body = Encoding.UTF8.GetBytes(payload);

            channel.BasicPublish(
                exchange: _config.ExchangeName,
                routingKey: routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);
        }
        catch (Exception ex)
        {
            _logger.ErrorAsync($"RabbitMqPublisher: failed to publish {routingKey} (eventId={eventId})", ex).GetAwaiter().GetResult();
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_connectionLock)
        {
            if (_connection is not null)
            {
                try { _connection.Close(); } catch { /* best-effort */ }
                _connection.Dispose();
                _connection = null;
            }
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    #endregion
}
