using System.Text.Json.Serialization;

namespace Nop.Core.Domain.Integration;

/// <summary>
/// Published by the WMS (OpenBoxes) when a shipment is dispatched and
/// a tracking number is assigned. Consumed by TrackingUpdatedHandler
/// to update the nopCommerce Shipment record (QAS-4).
///
/// FIX: Added ShippedAtUtc and OccurredAtUtc as aliases for StatusDateUtc
/// so the QAS-4 script payload (which uses these field names) is properly
/// deserialized without requiring changes to the test script.
/// </summary>
public class TrackingUpdatedEvent
{
    /// <summary>Stable event id used for idempotency (ADR-005).</summary>
    public Guid EventId { get; set; }

    /// <summary>nopCommerce Order.Id this shipment belongs to.</summary>
    public int OrderId { get; set; }

    /// <summary>nopCommerce Shipment.Id being updated (0 when not supplied by WMS).</summary>
    public int ShipmentId { get; set; }

    /// <summary>Carrier tracking number.</summary>
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>Tracking URL from the carrier.</summary>
    public string TrackingUrl { get; set; } = string.Empty;

    /// <summary>Status: Shipped, InTransit, Delivered, Exception.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>UTC moment the status was recorded.</summary>
    public DateTime StatusDateUtc { get; set; }

    /// <summary>
    /// Alias for StatusDateUtc used by the QAS-4 test script payload.
    /// When deserialized, populates StatusDateUtc if it was not already set.
    /// </summary>
    [JsonPropertyName("shippedAtUtc")]
    public DateTime? ShippedAtUtc
    {
        get => StatusDateUtc == default ? null : StatusDateUtc;
        set { if (value.HasValue && StatusDateUtc == default) StatusDateUtc = value.Value; }
    }

    /// <summary>
    /// Alias for StatusDateUtc used by the QAS-4 test script payload
    /// (occurredAtUtc field). Populated when shippedAtUtc is absent.
    /// </summary>
    [JsonPropertyName("occurredAtUtc")]
    public DateTime? OccurredAtUtc
    {
        get => StatusDateUtc == default ? null : StatusDateUtc;
        set { if (value.HasValue && StatusDateUtc == default) StatusDateUtc = value.Value; }
    }

    /// <summary>Carrier name (UPS, FedEx, DHL, etc.).</summary>
    public string Carrier { get; set; } = string.Empty;
}