using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nop.Core.Configuration;
using Nop.Core.Infrastructure;

namespace Nop.Services.Inventory;

public partial class InventoryStartup : INopStartup
{
    public virtual void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        var appSettings = Singleton<AppSettings>.Instance;
        services.AddSingleton(appSettings.Get<VerdeMartConfig>());

        services.AddScoped<IStockLedgerService, StockLedgerService>();
        services.AddTransient<StalenessCheckerTask>();
    }

    public virtual void Configure(IApplicationBuilder application)
    {
    }

    public int Order => 51;
}
