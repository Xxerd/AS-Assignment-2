using Nop.Core.Domain.Integration;
using Nop.Data;
using Nop.Services.Integration.Messaging;
using Nop.Services.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Services.Integration.Outbox;

/// <summary>
/// ScheduleTask that drains the OutboxMessage table and publishes rows
/// to RabbitMQ. Runs at the interval registered in the ScheduleTask
/// row (default 30 s).
///
/// At-least-once delivery: a row is marked published only after a
/// successful publish. If the publish throws, the row stays pending
/// and is retried on the next tick.
/// </summary>
public partial class OutboxPublisherTask : IScheduleTask
{
    /// <summary>Maximum rows processed per tick.</summary>
    protected const int BatchSize = 100;

    /// <summary>Stop retrying after this many attempts. Operator-driven recovery from here.</summary>
    protected const int MaxAttempts = 100;

    protected readonly IRepository<OutboxMessage> _outboxRepository;
    protected readonly IRabbitMqPublisher _publisher;
    protected readonly ILogger _logger;

    public OutboxPublisherTask(
        IRepository<OutboxMessage> outboxRepository,
        IRabbitMqPublisher publisher,
        ILogger logger)
    {
        _outboxRepository = outboxRepository;
        _publisher = publisher;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync()
    {
        var pending = _outboxRepository.Table
            .Where(m => m.PublishedOnUtc == null && m.Attempts < MaxAttempts)
            .OrderBy(m => m.Id)
            .Take(BatchSize)
            .ToList();

        if (pending.Count == 0)
            return;

        foreach (var row in pending)
        {
            try
            {
                await _publisher.PublishAsync(
                    routingKey: row.EventType,
                    payload: row.Payload,
                    eventId: row.EventId);

                row.PublishedOnUtc = DateTime.UtcNow;
                row.LastError = null;
                await _outboxRepository.UpdateAsync(row, publishEvent: false);
            }
            catch (Exception ex)
            {
                row.Attempts += 1;
                row.LastError = ex.Message;

                try
                {
                    await _outboxRepository.UpdateAsync(row, publishEvent: false);
                }
                catch { /* swallow secondary failure; row stays pending */ }

                await _logger.ErrorAsync(
                    $"OutboxPublisherTask: failed to publish row Id={row.Id} EventType={row.EventType} (attempt {row.Attempts})",
                    ex);
            }
        }
    }
}
