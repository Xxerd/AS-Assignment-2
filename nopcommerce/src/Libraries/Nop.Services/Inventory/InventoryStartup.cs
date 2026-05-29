using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Infrastructure;

namespace Nop.Services.Inventory;

/// <summary>
/// Registers VerdeMart inventory module services on application startup.
/// Follows the same INopStartup pattern as IntegrationStartup.
/// </summary>
public partial class InventoryStartup : INopStartup
{
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IStockLedgerService, StockLedgerService>();
        services.AddTransient<StalenessCheckerTask>();
    }

    public virtual void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 51;
}
