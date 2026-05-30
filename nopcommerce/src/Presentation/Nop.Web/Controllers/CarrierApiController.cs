using Microsoft.AspNetCore.Mvc;
using Nop.Services.Carriers;

namespace Nop.Web.Controllers;

/// <summary>
/// Demo / QAS-3 endpoint. Returns the current shipping rate (live or fallback)
/// and exposes the circuit breaker state for observability.
/// </summary>
[ApiController]
[Route("api/carrier")]
public class CarrierApiController : ControllerBase
{
    protected readonly ICarrierAdapter _carrierAdapter;
    protected readonly ICircuitBreakerStateMonitor _monitor;

    public CarrierApiController(ICarrierAdapter carrierAdapter, ICircuitBreakerStateMonitor monitor)
    {
        _carrierAdapter = carrierAdapter;
        _monitor = monitor;
    }

    /// <summary>
    /// Returns the shipping rate for the given zone.
    /// When the Carrier API is degraded, returns the cached fallback and
    /// marks isFallback=true so the caller knows the source.
    /// </summary>
    [HttpGet("rate")]
    public async Task<IActionResult> GetRate([FromQuery] string zone = "EU")
    {
        var rate = await _carrierAdapter.GetRateAsync(zone);
        return Ok(new
        {
            carrier = rate.Carrier,
            service = rate.Service,
            amount = rate.Amount,
            currency = rate.Currency,
            estimatedDeliveryDays = rate.EstimatedDeliveryDays,
            isFallback = rate.IsFallback,
            circuitState = _monitor.State.ToString()
        });
    }

    /// <summary>Returns the current circuit breaker state.</summary>
    [HttpGet("circuit")]
    public IActionResult GetCircuitState()
    {
        var secondsInState = (int)(DateTime.UtcNow - _monitor.StateChangedAtUtc).TotalSeconds;
        return Ok(new
        {
            state = _monitor.State.ToString(),
            stateChangedAtUtc = _monitor.StateChangedAtUtc,
            secondsInState
        });
    }
}
