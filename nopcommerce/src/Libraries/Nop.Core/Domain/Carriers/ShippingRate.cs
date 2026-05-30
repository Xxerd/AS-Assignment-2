namespace Nop.Core.Domain.Carriers;

/// <summary>
/// A shipping rate quote returned by the Carrier API or its fallback.
/// </summary>
public class ShippingRate
{
    public string Carrier { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "EUR";
    public int EstimatedDeliveryDays { get; set; }

    /// <summary>True when this rate came from the fallback cache, not the live API.</summary>
    public bool IsFallback { get; set; }
}
