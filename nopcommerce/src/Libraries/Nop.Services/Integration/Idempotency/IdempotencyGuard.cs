using Nop.Core.Domain.Integration;
using Nop.Data;

namespace Nop.Services.Integration.Idempotency;

/// <summary>
/// Default <see cref="IIdempotencyGuard"/> implementation backed by the
/// ProcessedEvent table. Scoped lifetime so it shares the ambient LinqToDb
/// transaction opened by the caller.
/// </summary>
public partial class IdempotencyGuard : IIdempotencyGuard
{
    protected readonly IRepository<ProcessedEvent> _repository;

    public IdempotencyGuard(IRepository<ProcessedEvent> repository)
    {
        _repository = repository;
    }

    public virtual async Task<bool> HasSeenAsync(Guid eventId)
    {
        return await _repository.Table
            .AnyAsync(e => e.EventId == eventId);
    }

    public virtual async Task MarkProcessedAsync(Guid eventId)
    {
        await _repository.InsertAsync(new ProcessedEvent
        {
            EventId = eventId,
            ProcessedAtUtc = DateTime.UtcNow
        }, publishEvent: false);
    }
}
