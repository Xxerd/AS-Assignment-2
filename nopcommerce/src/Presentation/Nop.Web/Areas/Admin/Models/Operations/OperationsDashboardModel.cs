using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Operations;

public partial record OperationsDashboardModel : BaseNopModel
{
    public OperationsDashboardModel()
    {
        StaleStockRows = new List<StaleStockRowModel>();
        OutboxDepthRows = new List<OutboxDepthRowModel>();
    }

    // Panel 1: Stale stock
    public IList<StaleStockRowModel> StaleStockRows { get; set; }
    public bool HasStaleStock => StaleStockRows.Any();

    // Panel 2: Outbox depth
    public IList<OutboxDepthRowModel> OutboxDepthRows { get; set; }
    public int TotalPendingOutbox { get; set; }
    public bool OutboxAlertActive { get; set; }
    public DateTime? OldestUnpublishedCreatedAt { get; set; }

    // Panel 3: Circuit breaker
    public string CircuitState { get; set; } = "Closed";
    public DateTime CircuitStateChangedAtUtc { get; set; }
    public int CircuitSecondsInState => (int)(DateTime.UtcNow - CircuitStateChangedAtUtc).TotalSeconds;
    public string CircuitStateColor => CircuitState switch
    {
        "Closed" => "green",
        "Open" => "red",
        "HalfOpen" => "orange",
        _ => "gray"
    };

    public bool AnyAlert => HasStaleStock || OutboxAlertActive || CircuitState != "Closed";
}

public partial record StaleStockRowModel : BaseNopEntityModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int ReservedQuantity { get; set; }
    public int AvailableQuantity => StockQuantity - ReservedQuantity;
    public DateTime LastUpdatedAtUtc { get; set; }
    public string LastUpdatedAgo => GetRelativeTime(LastUpdatedAtUtc);
    
    private static string GetRelativeTime(DateTime utcTime)
    {
        var diff = DateTime.UtcNow - utcTime;
        if (diff.TotalMinutes < 1) return "just now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalDays < 1) return $"{(int)diff.TotalHours} hours ago";
        return $"{(int)diff.TotalDays} days ago";
    }
}

public partial record OutboxDepthRowModel
{
    public string EventType { get; set; } = string.Empty;
    public int PendingCount { get; set; }
    public DateTime OldestPendingUtc { get; set; }
    public bool IsAlert => (DateTime.UtcNow - OldestPendingUtc).TotalMinutes > 5;
}
