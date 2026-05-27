using Newtonsoft.Json;
using Nop.Core.Domain.Integration;
using Nop.Data;

namespace Nop.Services.Integration.Outbox;

/// <summary>
/// Default <see cref="IOutboxWriter"/> implementation.
///
/// Serialises the event with Newtonsoft.Json (already a transitive
/// dependency of nopCommerce, see <c>AppSettingsHelper</c>) and writes
/// it via the existing <see cref="IRepository{T}"/> abstraction so the
/// insert participates in whatever ambient LinqToDb transaction the
/// caller has open.
/// </summary>
public partial class OutboxWriter : IOutboxWriter
{
    protected readonly IRepository<OutboxMessage> _outboxRepository;

    public OutboxWriter(IRepository<OutboxMessage> outboxRepository)
    {
        _outboxRepository = outboxRepository;
    }

    public virtual async Task<Guid> EnqueueAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(@event);

        var eventId = TryReadEventId(@event) ?? Guid.NewGuid();
        var payload = JsonConvert.SerializeObject(@event);

        var row = new OutboxMessage
        {
            EventId = eventId,
            EventType = typeof(TEvent).Name,
            Payload = payload,
            CreatedOnUtc = DateTime.UtcNow,
            PublishedOnUtc = null,
            Attempts = 0
        };

        await _outboxRepository.InsertAsync(row, publishEvent: false);
        return eventId;
    }

    /// <summary>
    /// If the event already carries an EventId property (Guid), reuse it
    /// so the same id is visible across the producer, the outbox row,
    /// and the broker message envelope.
    /// </summary>
    protected virtual Guid? TryReadEventId<TEvent>(TEvent @event)
    {
        var prop = typeof(TEvent).GetProperty("EventId");
        if (prop is null || prop.PropertyType != typeof(Guid))
            return null;

        var value = (Guid?)prop.GetValue(@event);
        return value is null || value == Guid.Empty ? null : value;
    }
}
