using Microsoft.Extensions.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Services.Carriers.Tasks;

/// <summary>
/// Refreshes the per-zone carrier rate cache every 15 minutes.
///
/// FIX (QAS-3): Always calls GetRateAsync regardless of circuit state.
/// When the circuit is Open, Polly will allow a single probe once the
/// BreakDuration elapses — that probe is what transitions the circuit to
/// HalfOpen and then Closed. Skipping the call when Open prevented Polly
/// from ever probing, so the circuit stayed Open permanently.
///
/// If the probe fails the circuit stays Open; if it succeeds the circuit
/// closes and the live rate is written back to the fallback cache.
/// </summary>
public partial class CarrierRateRefreshTask : IScheduleTask
{
    protected readonly ICarrierAdapter _carrierAdapter;
    protected readonly IRateFallbackProvider _fallbackProvider;
    protected readonly ICircuitBreakerStateMonitor _monitor;
    protected readonly ILogger<CarrierRateRefreshTask> _logger;

    public CarrierRateRefreshTask(
        ICarrierAdapter carrierAdapter,
        IRateFallbackProvider fallbackProvider,
        ICircuitBreakerStateMonitor monitor,
        ILogger<CarrierRateRefreshTask> logger)
    {
        _carrierAdapter = carrierAdapter;
        _fallbackProvider = fallbackProvider;
        _monitor = monitor;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync()
    {
        // Always call — even when the circuit is Open.
        // If BreakDuration has elapsed, Polly will transition to HalfOpen
        // and allow this probe through. A successful probe closes the circuit.
        // A failed probe resets the BreakDuration and keeps it Open.
        // Only log differently so operators can see the probe intent.
        var state = _monitor.State;
        if (state == CircuitState.Open)
            _logger.LogDebug("CarrierRateRefresh: circuit is Open — probing carrier to allow recovery");

        try
        {
            var rate = await _carrierAdapter.GetRateAsync("EU");
            if (!rate.IsFallback)
            {
                _fallbackProvider.UpdateCache("EU", rate);
                _logger.LogDebug("CarrierRateRefresh: cached rate EUR {Amount} for zone EU", rate.Amount);
            }
            else
            {
                _logger.LogDebug("CarrierRateRefresh: fallback rate returned — circuit still recovering");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CarrierRateRefresh: probe failed — circuit remains Open");
        }
    }
}