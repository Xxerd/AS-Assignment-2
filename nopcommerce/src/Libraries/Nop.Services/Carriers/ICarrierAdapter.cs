using Nop.Core.Domain.Carriers;

namespace Nop.Services.Carriers;

/// <summary>
/// Fetches shipping rates from the external Carrier API.
/// Implementations wrap the call in a circuit breaker and fall back to
/// cached rates when the carrier is degraded (QAS-3).
/// </summary>
public interface ICarrierAdapter
{
    Task<ShippingRate> GetRateAsync(string zone = "EU", CancellationToken cancellationToken = default);
}
