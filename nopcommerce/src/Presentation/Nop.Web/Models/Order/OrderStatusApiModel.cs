namespace Nop.Web.Models.Order;

/// <summary>
/// Cross-channel order status response for QAS-4.
/// Used by POS terminal to look up online orders.
/// </summary>
public class OrderStatusApiModel
{
    public int OrderId { get; set; }
    public Guid OrderGuid { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string ShippingStatus { get; set; } = string.Empty;
    
    // Shipment info from WMS
    public List<ShipmentTrackingModel> Shipments { get; set; } = new();
    
    // Cross-channel visibility timestamps
    public DateTime CreatedOnUtc { get; set; }
    public DateTime? PaidOnUtc { get; set; }
    public DateTime? ShippedOnUtc { get; set; }
    public DateTime? DeliveredOnUtc { get; set; }
    
    // Customer info for in-store lookup
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    
    // Channel origin
    public string Channel { get; set; } = "web";
}

public class ShipmentTrackingModel
{
    public int ShipmentId { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public string TrackingUrl { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ShippedDateUtc { get; set; }
    public DateTime? DeliveredDateUtc { get; set; }
    public string Carrier { get; set; } = string.Empty;
}