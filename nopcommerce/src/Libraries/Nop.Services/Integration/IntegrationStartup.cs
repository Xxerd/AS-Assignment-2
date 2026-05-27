using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;
using Nop.Services.Integration.Messaging;

namespace Nop.Services.Integration;

/// <summary>
/// Registers VerdeMart integration-layer services on application startup.
///
/// Phase 1 scope: RabbitMQ publisher singleton + RabbitMqConfig binding.
/// Outbox writer, publisher task, and inbound consumers land in later phases.
/// </summary>
public partial class IntegrationStartup : INopStartup
{
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var appSettings = Singleton<AppSettings>.Instance;
        var rabbitConfig = appSettings.Get<RabbitMqConfig>();
        services.AddSingleton(rabbitConfig);

        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
    }

    public virtual void Configure(IApplicationBuilder application)
    {
    }

    /// <summary>
    /// Run after data layer (10) and before web infrastructure so other services can resolve the publisher.
    /// </summary>
    public int Order => 50;
}
