using Nop.Core.Domain.Integration;
using Nop.Services.Integration.Idempotency;
using Nop.Services.Inventory;

namespace Nop.Services.Integration.Consumers;

/// <summary>
/// Applies a WMS pick event to the local StockLedger.
///
/// Idempotency (ADR-005): if ProcessedEvent already contains the EventId,
/// the message is silently skipped — safe for RabbitMQ redelivery.
/// </summary>
public partial class WmsStockPickedHandler : IIntegrationEventHandler<StockPickedEvent>
{
    protected readonly IIdempotencyGuard _idempotencyGuard;
    protected readonly IStockLedgerService _stockLedgerService;

    public WmsStockPickedHandler(
        IIdempotencyGuard idempotencyGuard,
        IStockLedgerService stockLedgerService)
    {
        _idempotencyGuard = idempotencyGuard;
        _stockLedgerService = stockLedgerService;
    }

    public virtual async Task HandleAsync(StockPickedEvent @event, CancellationToken cancellationToken = default)
    {
        if (await _idempotencyGuard.HasSeenAsync(@event.EventId))
            return;

        await _stockLedgerService.ApplyPickAsync(
            @event.ProductId,
            @event.WarehouseId,
            @event.QuantityPicked,
            @event.OccurredAtUtc);

        await _idempotencyGuard.MarkProcessedAsync(@event.EventId);
    }
}
