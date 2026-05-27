namespace Nop.Services.Integration.Outbox;

/// <summary>
/// Writes integration events to the OutboxMessage table. The
/// OutboxPublisherTask drains rows from this table and delivers them
/// to RabbitMQ.
///
/// Each call persists exactly one row. Callers are responsible for
/// keeping the call in the same logical unit-of-work as the business
/// state being committed (ADR-002, Transactional Outbox).
/// </summary>
public partial interface IOutboxWriter
{
    /// <summary>
    /// Enqueue a typed event. Serialised to JSON and stored as a row.
    /// Returns the generated event id (Guid) so the caller can correlate
    /// the row with the originating business operation.
    /// </summary>
    Task<Guid> EnqueueAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;
}
