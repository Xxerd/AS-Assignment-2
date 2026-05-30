using Nop.Core.Domain.Carriers;

namespace Nop.Services.Carriers;

/// <summary>
/// Provides a cached shipping rate when the live Carrier API is unreachable.
/// Updated by CarrierRateRefreshTask while the circuit is closed.
/// </summary>
public interface IRateFallbackProvider
{
    ShippingRate GetFallbackRate(string zone = "EU");
    void UpdateCache(string zone, ShippingRate rate);
}
