namespace Nop.Services.Carriers;

public partial class CircuitBreakerStateMonitor : ICircuitBreakerStateMonitor
{
    private volatile CircuitState _state = CircuitState.Closed;
    private DateTime _stateChangedAtUtc = DateTime.UtcNow;

    public CircuitState State => _state;
    public DateTime StateChangedAtUtc => _stateChangedAtUtc;

    public void SetState(CircuitState state)
    {
        _state = state;
        _stateChangedAtUtc = DateTime.UtcNow;
    }
}
