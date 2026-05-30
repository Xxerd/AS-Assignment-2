using Nop.Core.Configuration;
using Nop.Services.ScheduleTasks;

namespace Nop.Services.Inventory;

public partial class StalenessCheckerTask : IScheduleTask
{
    protected readonly IStockLedgerService _stockLedgerService;
    protected readonly VerdeMartConfig _verdeMartConfig;

    public StalenessCheckerTask(
        IStockLedgerService stockLedgerService,
        VerdeMartConfig verdeMartConfig)
    {
        _stockLedgerService = stockLedgerService;
        _verdeMartConfig = verdeMartConfig;
    }

    public virtual async Task ExecuteAsync()
    {
        var threshold = TimeSpan.FromMinutes(_verdeMartConfig.StalenessThresholdMinutes);
        await _stockLedgerService.MarkStaleEntriesAsync(threshold);
    }
}
