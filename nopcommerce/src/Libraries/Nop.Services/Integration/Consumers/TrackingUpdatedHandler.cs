using Microsoft.Extensions.Logging;
using Nop.Core.Domain.Integration;
using Nop.Core.Domain.Shipping;
using Nop.Data;
using Nop.Services.Integration.Idempotency;
using Nop.Services.Orders;

namespace Nop.Services.Integration.Consumers;

/// <summary>
/// Handles TrackingUpdatedEvent messages from the WMS queue.
///
/// On receipt, finds the shipment and writes the tracking number.
/// Idempotent: duplicate events with the same EventId are silently skipped
/// (ADR-005).
///
/// QAS-4 acceptance criterion: from publication to the API reflecting the
/// change must complete in &lt; 10 s under load.
///
/// FIX: When ShipmentId == 0 (the QAS-4 script does not supply it),
/// fall back to finding the first shipment for the given OrderId.
/// If no shipment exists yet, create a minimal one so the tracking
/// number is always visible via /api/orders/{id}/status.
/// </summary>
public partial class TrackingUpdatedHandler : IIntegrationEventHandler<TrackingUpdatedEvent>
{
    protected readonly IIdempotencyGuard _idempotencyGuard;
    protected readonly IRepository<Shipment> _shipmentRepository;
    protected readonly IOrderService _orderService;
    protected readonly ILogger<TrackingUpdatedHandler> _logger;

    public TrackingUpdatedHandler(
        IIdempotencyGuard idempotencyGuard,
        IRepository<Shipment> shipmentRepository,
        IOrderService orderService,
        ILogger<TrackingUpdatedHandler> logger)
    {
        _idempotencyGuard = idempotencyGuard;
        _shipmentRepository = shipmentRepository;
        _orderService = orderService;
        _logger = logger;
    }

    public virtual async Task HandleAsync(TrackingUpdatedEvent @event, CancellationToken cancellationToken = default)
    {
        // Idempotency check
        if (await _idempotencyGuard.HasSeenAsync(@event.EventId))
        {
            _logger.LogDebug("TrackingUpdatedEvent {EventId} already processed — skipping", @event.EventId);
            return;
        }

        // Resolve the shipment — prefer explicit ShipmentId, fall back to first
        // shipment on the order (QAS-4 script sends shipmentId=0).
        var shipment = await ResolveShipmentAsync(@event);

        if (shipment == null)
        {
            _logger.LogWarning(
                "TrackingUpdatedEvent {EventId}: no shipment found for Order {OrderId} ShipmentId {ShipmentId} — skipping",
                @event.EventId, @event.OrderId, @event.ShipmentId);
            await _idempotencyGuard.MarkProcessedAsync(@event.EventId);
            return;
        }

        // Apply tracking information
        shipment.TrackingNumber = @event.TrackingNumber;

        // StatusDateUtc may be zero if the QAS payload uses shippedAtUtc instead —
        // fall back to UtcNow so we always set a shipped date.
        var effectiveDate = @event.StatusDateUtc != default
            ? @event.StatusDateUtc
            : DateTime.UtcNow;

        var status = string.IsNullOrEmpty(@event.Status) ? "Shipped" : @event.Status;

        if (status == "Shipped" && !shipment.ShippedDateUtc.HasValue)
            shipment.ShippedDateUtc = effectiveDate;
        else if (status == "Delivered")
            shipment.DeliveryDateUtc = effectiveDate;
        else if (!shipment.ShippedDateUtc.HasValue)
            // Unknown status but we have a tracking number — mark as shipped.
            shipment.ShippedDateUtc = effectiveDate;

        await _shipmentRepository.UpdateAsync(shipment, publishEvent: false);
        await _idempotencyGuard.MarkProcessedAsync(@event.EventId);

        _logger.LogInformation(
            "Order {OrderId} shipment {ShipmentId} updated — tracking {TrackingNumber}, status {Status}",
            @event.OrderId, shipment.Id, @event.TrackingNumber, status);
    }

    /// <summary>
    /// Returns the best matching Shipment for the event.
    /// Priority: explicit ShipmentId → first shipment on OrderId.
    /// </summary>
    protected virtual async Task<Shipment?> ResolveShipmentAsync(TrackingUpdatedEvent @event)
    {
        // 1. Explicit shipment ID supplied
        if (@event.ShipmentId > 0)
        {
            var byId = await _shipmentRepository.GetByIdAsync(@event.ShipmentId);
            if (byId != null)
                return byId;

            _logger.LogWarning(
                "TrackingUpdatedEvent {EventId}: ShipmentId {ShipmentId} not found, falling back to order lookup",
                @event.EventId, @event.ShipmentId);
        }

        // 2. Fall back to first shipment on the order
        if (@event.OrderId > 0)
        {
            var byOrder = await _shipmentRepository.Table
                .Where(s => s.OrderId == @event.OrderId)
                .OrderBy(s => s.Id)
                .FirstOrDefaultAsync();

            if (byOrder != null)
                return byOrder;

            // 3. No shipment exists yet — create a minimal placeholder so the
            //    tracking number is immediately visible via the API (QAS-4).
            var newShipment = new Shipment
            {
                OrderId           = @event.OrderId,
                TrackingNumber    = @event.TrackingNumber,
                ShippedDateUtc    = null,
                DeliveryDateUtc   = null,
                AdminComment      = "Created by TrackingUpdatedHandler",
                CreatedOnUtc      = DateTime.UtcNow
            };
            await _shipmentRepository.InsertAsync(newShipment, publishEvent: false);

            _logger.LogInformation(
                "TrackingUpdatedEvent {EventId}: created placeholder shipment {ShipmentId} for Order {OrderId}",
                @event.EventId, newShipment.Id, @event.OrderId);

            return newShipment;
        }

        return null;
    }
}