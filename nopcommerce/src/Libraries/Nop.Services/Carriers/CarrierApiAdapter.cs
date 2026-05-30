using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Nop.Core.Configuration;
using Nop.Core.Domain.Carriers;
using Polly;
using Polly.CircuitBreaker;

namespace Nop.Services.Carriers;

/// <summary>
/// Calls the external Carrier API and wraps the call in a Polly circuit breaker.
///
/// Circuit states (ADR-003):
///   Closed   — live API reachable, real rates returned
///   Open     — carrier degraded, fallback cache returned in &lt; 100 ms
///   HalfOpen — single probe; if it succeeds, circuit closes
///
/// Singleton: the pipeline must survive across requests so the circuit
/// state is shared across all concurrent checkout requests.
/// </summary>
public partial class CarrierApiAdapter : ICarrierAdapter
{
    protected readonly IRateFallbackProvider _fallback;
    protected readonly ICircuitBreakerStateMonitor _monitor;
    protected readonly ILogger<CarrierApiAdapter> _logger;
    protected readonly HttpClient _http;
    protected readonly ResiliencePipeline<ShippingRate?> _pipeline;

    public CarrierApiAdapter(
        CarrierConfig config,
        IRateFallbackProvider fallback,
        ICircuitBreakerStateMonitor monitor,
        ILogger<CarrierApiAdapter> logger)
    {
        _fallback = fallback;
        _monitor = monitor;
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl),
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds + 1)
        };

        _pipeline = new ResiliencePipelineBuilder<ShippingRate?>()
            .AddTimeout(TimeSpan.FromSeconds(config.TimeoutSeconds))
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<ShippingRate?>
            {
                ShouldHandle = new PredicateBuilder<ShippingRate?>().Handle<Exception>(),
                // ADR-003: 3 failures or 50% failure rate over 10 s window
                MinimumThroughput = config.CircuitBreakerFailuresBeforeBreaking,
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(10),
                BreakDuration = TimeSpan.FromSeconds(config.CircuitBreakerDurationSeconds),
                OnOpened = args =>
                {
                    _monitor.SetState(CircuitState.Open);
                    _logger.LogWarning("Carrier circuit OPENED — fallback rates active for {Duration}s",
                        config.CircuitBreakerDurationSeconds);
                    return ValueTask.CompletedTask;
                },
                OnClosed = args =>
                {
                    _monitor.SetState(CircuitState.Closed);
                    _logger.LogInformation("Carrier circuit CLOSED — live rates resumed");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = args =>
                {
                    _monitor.SetState(CircuitState.HalfOpen);
                    _logger.LogInformation("Carrier circuit HALF-OPEN — probing carrier");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public virtual async Task<ShippingRate> GetRateAsync(string zone = "EU", CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _pipeline.ExecuteAsync(async ct =>
            {
                var response = await _http.PostAsJsonAsync("/v1/rates", new { zone }, ct);
                response.EnsureSuccessStatusCode();
                var dto = await response.Content.ReadFromJsonAsync<CarrierRateDto>(
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);
                return (ShippingRate?)new ShippingRate
                {
                    Carrier = dto!.Carrier,
                    Service = dto.Service,
                    Amount = dto.Amount,
                    Currency = dto.Currency,
                    EstimatedDeliveryDays = dto.EstimatedDeliveryDays,
                    IsFallback = false
                };
            }, cancellationToken);

            return result!;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogDebug("Carrier circuit open — returning fallback rate for zone {Zone}", zone);
            return _fallback.GetFallbackRate(zone);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Carrier API call failed — returning fallback rate for zone {Zone}", zone);
            return _fallback.GetFallbackRate(zone);
        }
    }

    private sealed class CarrierRateDto
    {
        public string Carrier { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "EUR";
        public int EstimatedDeliveryDays { get; set; }
    }
}
