using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Domain.Integration;
using Nop.Core.Infrastructure;
using Nop.Services.Carriers;
using Nop.Services.Carriers.Tasks;
using Nop.Services.Integration.Consumers;
using Nop.Services.Integration.Idempotency;
using Nop.Services.Integration.Messaging;
using Nop.Services.Integration.Outbox;

namespace Nop.Services.Integration;

/// <summary>
/// Registers VerdeMart integration-layer services on application startup.
///
/// Phase 1: RabbitMQ publisher singleton + RabbitMqConfig binding.
/// Phase 2: outbox writer (scoped, shares the ambient DB transaction)
///          and the publisher task (transient — one per tick).
/// Phase 4.5: TrackingUpdatedHandler for QAS-4 cross-channel order visibility.
/// IConsumer&lt;T&gt; implementations are auto-registered by NopStartup.
/// </summary>
public partial class IntegrationStartup : INopStartup
{
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var appSettings = Singleton<AppSettings>.Instance;
        var rabbitConfig = appSettings.Get<RabbitMqConfig>();
        services.AddSingleton(rabbitConfig);

        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddTransient<OutboxPublisherTask>();

        services.AddScoped<IIdempotencyGuard, IdempotencyGuard>();

        services.AddScoped<IIntegrationEventHandler<StockPickedEvent>, WmsStockPickedHandler>();
        // Phase 4.5 — QAS-4: WMS shipment dispatched → update Shipment.TrackingNumber
        services.AddScoped<IIntegrationEventHandler<TrackingUpdatedEvent>, TrackingUpdatedHandler>();
        services.AddHostedService<RabbitMqConsumerHostedService>();

        // Carrier circuit breaker (Block E / QAS-3)
        var carrierConfig = appSettings.Get<CarrierConfig>();
        services.AddSingleton(carrierConfig);
        services.AddSingleton<ICircuitBreakerStateMonitor, CircuitBreakerStateMonitor>();
        services.AddSingleton<IRateFallbackProvider, CachedRateFallbackProvider>();
        services.AddSingleton<ICarrierAdapter, CarrierApiAdapter>();
        services.AddTransient<CarrierRateRefreshTask>();
    }

    public virtual void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Run after data layer (10) and before web infrastructure so other services can resolve the publisher.
    /// </summary>
    public int Order => 50;
}