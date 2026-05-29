namespace Nop.Services.Integration.Idempotency;

/// <summary>
/// Guards inbound integration event consumers against duplicate processing
/// (ADR-005). Every inbound RabbitMQ consumer must call HasSeenAsync before
/// applying any business change, and MarkProcessedAsync in the same DB
/// transaction after applying it.
/// </summary>
public partial interface IIdempotencyGuard
{
    /// <summary>
    /// Returns true if this EventId has already been successfully processed.
    /// </summary>
    Task<bool> HasSeenAsync(Guid eventId);

    /// <summary>
    /// Records this EventId as processed. Must be called in the same
    /// transaction as the business write it protects.
    /// </summary>
    Task MarkProcessedAsync(Guid eventId);
}
