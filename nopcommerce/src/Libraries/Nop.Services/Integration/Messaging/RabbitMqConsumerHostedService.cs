using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nop.Core.Configuration;
using Nop.Core.Domain.Integration;
using Nop.Services.Integration.Consumers;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.Services.Integration.Messaging;

/// <summary>
/// Long-lived IHostedService that subscribes to the WMS queue on the
/// commerce.events topic exchange and routes each message to the
/// appropriate IIntegrationEventHandler.
///
/// Resilience: if the broker is unreachable on startup, connection is
/// retried every 10 seconds in the background. AutomaticRecoveryEnabled
/// handles drops after a successful initial connection.
///
/// Each message is processed inside a fresh DI scope so that scoped
/// services (IStockLedgerService, IIdempotencyGuard) get a clean unit
/// of work. Messages are ACK'd only after the handler succeeds; on
/// failure they are NACK'd without requeue to avoid poison-message loops.
///
/// Phase 4.5: binding key broadened from "wms.stock.#" to "wms.#" so
/// that "wms.shipment.dispatched" events are also received and routed
/// to TrackingUpdatedHandler (QAS-4).
/// </summary>
public partial class RabbitMqConsumerHostedService : IHostedService, IDisposable
{
    protected const string QueueName = "verdemart.nopcommerce.wms";
    // Broadened from "wms.stock.#" to "wms.#" to also capture shipment events (Phase 4.5)
    protected const string BindingKey = "wms.#";
    protected const string StockPickedKey = "wms.stock.picked";
    protected const string TrackingUpdatedKey = "wms.shipment.dispatched";

    protected readonly RabbitMqConfig _config;
    protected readonly IServiceScopeFactory _scopeFactory;
    protected readonly ILogger<RabbitMqConsumerHostedService> _logger;
    protected IConnection _connection;
    protected IModel _channel;
    protected CancellationTokenSource _cts;

    public RabbitMqConsumerHostedService(
        RabbitMqConfig config,
        IServiceScopeFactory scopeFactory,
        ILogger<RabbitMqConsumerHostedService> logger)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_config.Enabled)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = Task.Run(() => ConnectWithRetryAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        try { _channel?.Close(); } catch { }
        try { _connection?.Close(); } catch { }
        return Task.CompletedTask;
    }

    protected virtual async Task ConnectWithRetryAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
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

                _connection = factory.CreateConnection("verdemart-consumer");
                _channel = _connection.CreateModel();

                _channel.ExchangeDeclare(
                    exchange: _config.ExchangeName,
                    type: ExchangeType.Topic,
                    durable: true,
                    autoDelete: false);

                _channel.QueueDeclare(
                    queue: QueueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false);

                _channel.QueueBind(
                    queue: QueueName,
                    exchange: _config.ExchangeName,
                    routingKey: BindingKey);

                // one message at a time — back-pressure against the DB
                _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += OnMessageReceivedAsync;

                _channel.BasicConsume(
                    queue: QueueName,
                    autoAck: false,
                    consumer: consumer);

                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "RabbitMQ consumer: connection failed, retrying in 10s");
                await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
            }
        }
    }

    protected virtual async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var routingKey = ea.RoutingKey;
        var json = Encoding.UTF8.GetString(ea.Body.ToArray());
        var success = false;

        try
        {
            using var scope = _scopeFactory.CreateScope();

            if (routingKey == StockPickedKey)
            {
                var @event = JsonSerializer.Deserialize<StockPickedEvent>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var handler = scope.ServiceProvider
                    .GetRequiredService<IIntegrationEventHandler<StockPickedEvent>>();

                await handler.HandleAsync(@event);
            }
            else if (routingKey == TrackingUpdatedKey)
            {
                // Phase 4.5 — QAS-4: WMS shipment dispatched event
                var @event = JsonSerializer.Deserialize<TrackingUpdatedEvent>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                var handler = scope.ServiceProvider
                    .GetRequiredService<IIntegrationEventHandler<TrackingUpdatedEvent>>();

                await handler.HandleAsync(@event);
            }
            else
            {
                _logger.LogDebug("RabbitMQ consumer: unhandled routing key '{RoutingKey}' — message discarded", routingKey);
            }

            success = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RabbitMQ consumer: failed to process '{RoutingKey}'", routingKey);
        }
        finally
        {
            if (success)
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
            else
                _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
        }
    }

    public void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}