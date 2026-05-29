using Nop.Core.Domain.Integration;
using Nop.Services.Integration.Idempotency;
using Nop.Services.Inventory;
using Nop.Services.Logging;

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
    protected readonly ILogger _logger;

    public WmsStockPickedHandler(
        IIdempotencyGuard idempotencyGuard,
        IStockLedgerService stockLedgerService,
        ILogger logger)
    {
        _idempotencyGuard = idempotencyGuard;
        _stockLedgerService = stockLedgerService;
        _logger = logger;
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

        await _logger.InformationAsync(
            $"WMS pick applied: product={@event.ProductId} warehouse={@event.WarehouseId} qty={@event.QuantityPicked}");
    }
}
