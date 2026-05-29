namespace Nop.Services.Integration.Consumers;

/// <summary>
/// Processes a single inbound integration event of type <typeparamref name="T"/>.
/// Implementations are resolved per-message inside a DI scope.
/// </summary>
public interface IIntegrationEventHandler<T>
{
    Task HandleAsync(T @event, CancellationToken cancellationToken = default);
}
