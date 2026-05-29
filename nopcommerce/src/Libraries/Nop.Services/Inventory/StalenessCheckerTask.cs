using Nop.Core.Configuration;
using Nop.Services.ScheduleTasks;

namespace Nop.Services.Inventory;

/// <summary>
/// Schedule task that marks StockLedgerEntry rows as stale when the WMS
/// has not sent an update within the configured threshold (default 10 min).
///
/// Staleness is an observable signal — the storefront shows an "as of"
/// indicator and the Ops Dashboard lists stale entries. This makes the
/// WMS silence visible rather than silently serving outdated stock numbers.
///
/// Runs every 60 seconds. The staleness threshold is read from
/// AppSettings (VerdeMartConfig.StalenessThresholdMinutes).
/// </summary>
public partial class StalenessCheckerTask : IScheduleTask
{
    protected readonly IStockLedgerService _stockLedgerService;
    protected readonly AppSettings _appSettings;

    public StalenessCheckerTask(
        IStockLedgerService stockLedgerService,
        AppSettings appSettings)
    {
        _stockLedgerService = stockLedgerService;
        _appSettings = appSettings;
    }

    public virtual async Task ExecuteAsync()
    {
        var config = _appSettings.Get<VerdeMartConfig>();
        var threshold = TimeSpan.FromMinutes(config.StalenessThresholdMinutes);
        await _stockLedgerService.MarkStaleEntriesAsync(threshold);
    }
}
