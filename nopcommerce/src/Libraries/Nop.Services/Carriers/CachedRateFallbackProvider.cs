using Nop.Core.Caching;
using Nop.Core.Domain.Carriers;

namespace Nop.Services.Carriers;

public partial class CachedRateFallbackProvider : IRateFallbackProvider
{
    private static readonly ShippingRate _defaultFallback = new()
    {
        Carrier = "UPS",
        Service = "Ground",
        Amount = 9.99m,  // conservative over-estimate — see ADR-003
        Currency = "EUR",
        EstimatedDeliveryDays = 5,
        IsFallback = true
    };

    // In-memory fallback for when cache is cold. Thread-safe via volatile + assignment.
    private volatile ShippingRate? _inMemoryRate;

    protected readonly IStaticCacheManager _cache;

    public CachedRateFallbackProvider(IStaticCacheManager cache)
    {
        _cache = cache;
    }

    public ShippingRate GetFallbackRate(string zone = "EU")
    {
        var cached = _inMemoryRate;
        if (cached is not null)
        {
            return new ShippingRate
            {
                Carrier = cached.Carrier,
                Service = cached.Service,
                Amount = cached.Amount,
                Currency = cached.Currency,
                EstimatedDeliveryDays = cached.EstimatedDeliveryDays,
                IsFallback = true
            };
        }
        return _defaultFallback;
    }

    public void UpdateCache(string zone, ShippingRate rate)
    {
        _inMemoryRate = rate;
    }
}
