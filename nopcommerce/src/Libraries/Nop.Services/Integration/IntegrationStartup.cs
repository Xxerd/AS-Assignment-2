using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Domain.Integration;
using Nop.Core.Infrastructure;
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
        services.AddHostedService<RabbitMqConsumerHostedService>();
    }

    public virtual void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Run after data layer (10) and before web infrastructure so other services can resolve the publisher.
    /// </summary>
    public int Order => 50;
}
