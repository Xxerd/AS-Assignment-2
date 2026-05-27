namespace Nop.Core.Domain.Integration;

/// <summary>
/// Outbox row used by the VerdeMart integration layer.
///
/// Each row is written in the same logical operation as the business
/// state it describes (an order, a stock reservation, …). The
/// <see cref="OutboxPublisherTask"/> drains unpublished rows and
/// delivers them to RabbitMQ, then sets <see cref="PublishedOnUtc"/>.
/// </summary>
public partial class OutboxMessage : BaseEntity
{
    /// <summary>
    /// Stable unique id of the domain event. Carried in the message
    /// envelope so downstream consumers can deduplicate (ADR-005).
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// Event type / routing key. By convention this is the .NET class
    /// name without the namespace (e.g. <c>OrderConfirmedEvent</c>).
    /// </summary>
    public string EventType { get; set; }

    /// <summary>
    /// JSON-serialized event payload.
    /// </summary>
    public string Payload { get; set; }

    /// <summary>
    /// UTC time the row was written.
    /// </summary>
    public DateTime CreatedOnUtc { get; set; }

    /// <summary>
    /// UTC time the row was successfully published to the broker.
    /// Null while the row is still pending.
    /// </summary>
    public DateTime? PublishedOnUtc { get; set; }

    /// <summary>
    /// Number of publish attempts. Incremented on every failure.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Last error message captured by the publisher, if any.
    /// </summary>
    public string LastError { get; set; }
}
