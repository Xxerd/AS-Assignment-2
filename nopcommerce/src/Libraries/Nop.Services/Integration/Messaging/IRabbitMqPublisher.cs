namespace Nop.Services.Integration.Messaging;

/// <summary>
/// Publishes commerce events from nopCommerce to RabbitMQ.
/// Phase 1: fire-and-forget. Durable delivery (outbox) lands in Phase 2.
/// </summary>
public partial interface IRabbitMqPublisher
{
    /// <summary>
    /// Publish a serialized message to the configured exchange.
    /// </summary>
    /// <param name="routingKey">Routing key. By convention this is the event type name (e.g. <c>OrderConfirmed</c>).</param>
    /// <param name="payload">Already-serialized JSON payload.</param>
    /// <param name="eventId">Unique event id (used downstream for deduplication).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishAsync(string routingKey, string payload, Guid eventId, CancellationToken cancellationToken = default);
}
