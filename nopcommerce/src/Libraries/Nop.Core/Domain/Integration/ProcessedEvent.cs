namespace Nop.Core.Domain.Integration;

/// <summary>
/// Deduplication record for inbound integration events (ADR-005).
///
/// Every inbound RabbitMQ message carries a unique EventId. Before
/// processing, the consumer checks this table. If the EventId is already
/// here, the message is a duplicate and is acked without re-processing.
/// If it is not here, the consumer processes the message and inserts a
/// row in the same DB transaction as the business write — so the guard
/// is atomic with the change it protects.
///
/// This makes all inbound consumers idempotent: receiving the same
/// message N times produces the same result as receiving it once.
/// </summary>
public partial class ProcessedEvent : BaseEntity
{
    /// <summary>
    /// Unique identifier of the domain event. Must be unique in this
    /// table — enforced by a DB unique index in the migration.
    /// </summary>
    public Guid EventId { get; set; }

    /// <summary>
    /// UTC time the event was first successfully processed.
    /// </summary>
    public DateTime ProcessedAtUtc { get; set; }
}
