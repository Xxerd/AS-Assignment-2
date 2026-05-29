namespace Nop.Core.Configuration;

/// <summary>
/// VerdeMart-specific configuration. Bound from the VerdeMartConfig section
/// of appsettings.json. Provides tuneable thresholds for the integration
/// layer and inventory module without requiring a code change.
/// </summary>
public partial class VerdeMartConfig : IConfig
{
    /// <summary>
    /// Minutes without a WMS update before a StockLedgerEntry is marked
    /// stale. Default: 10 minutes.
    /// </summary>
    public int StalenessThresholdMinutes { get; protected set; } = 10;

    /// <summary>
    /// Maximum units processed by the OutboxPublisherTask per tick.
    /// </summary>
    public int OutboxBatchSize { get; protected set; } = 100;
}
