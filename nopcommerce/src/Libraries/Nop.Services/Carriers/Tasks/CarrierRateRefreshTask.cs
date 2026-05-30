using Microsoft.Extensions.Logging;
using Nop.Services.ScheduleTasks;

namespace Nop.Services.Carriers.Tasks;

/// <summary>
/// Refreshes the per-zone carrier rate cache every 15 minutes while the
/// circuit is closed. If the circuit is open the task is a no-op —
/// the cache retains the last known good rate.
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
        if (_monitor.State == CircuitState.Open)
        {
            _logger.LogDebug("CarrierRateRefresh: circuit is open, skipping refresh");
            return;
        }

        try
        {
            var rate = await _carrierAdapter.GetRateAsync("EU");
            if (!rate.IsFallback)
            {
                _fallbackProvider.UpdateCache("EU", rate);
                _logger.LogDebug("CarrierRateRefresh: cached rate EUR {Amount} for zone EU", rate.Amount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CarrierRateRefresh: failed to refresh carrier rate cache");
        }
    }
}
