namespace Nop.Services.Carriers;

public enum CircuitState { Closed, Open, HalfOpen }

/// <summary>
/// Singleton that tracks the current state of the Carrier circuit breaker.
/// Read by the Ops Dashboard (Block F) to display carrier health.
/// </summary>
public interface ICircuitBreakerStateMonitor
{
    CircuitState State { get; }
    DateTime StateChangedAtUtc { get; }
    void SetState(CircuitState state);
}
