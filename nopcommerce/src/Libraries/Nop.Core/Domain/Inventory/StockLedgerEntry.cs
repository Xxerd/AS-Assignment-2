namespace Nop.Core.Domain.Inventory;

/// <summary>
/// Cross-channel stock record maintained by the VerdeMart inventory module.
///
/// The WMS (OpenBoxes) owns the physical stock truth. This entity is the
/// Commerce Core's local replica — updated asynchronously when the WMS
/// publishes StockPickedEvent messages. The Reservation API writes
/// ReservedQuantity atomically so web and POS channels cannot both sell
/// the same unit (QAS-2).
///
/// Available-to-sell = StockQuantity - ReservedQuantity.
///
/// IsStale is set by StalenessCheckerTask when LastUpdatedAtUtc is older
/// than the configured threshold, signalling that the WMS has not sent
/// updates recently and the numbers may be outdated.
/// </summary>
public partial class StockLedgerEntry : BaseEntity
{
    /// <summary>
    /// FK to nopCommerce Product.Id.
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// FK to nopCommerce Warehouse.Id. 0 = default / unspecified warehouse.
    /// </summary>
    public int WarehouseId { get; set; }

    /// <summary>
    /// Physical units confirmed by the WMS. Updated on every inbound
    /// StockPickedEvent from OpenBoxes.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>
    /// Units locked by the Reservation API but not yet converted into
    /// a placed order. Incremented on reservation, decremented on order
    /// placement or reservation release.
    /// </summary>
    public int ReservedQuantity { get; set; }

    /// <summary>
    /// UTC timestamp of the last WMS update. Used by StalenessCheckerTask
    /// to decide when to set IsStale = true.
    /// </summary>
    public DateTime LastUpdatedAtUtc { get; set; }

    /// <summary>
    /// True when the WMS has not sent an update within the staleness
    /// threshold (default 10 minutes). The UI shows an "as of" indicator
    /// rather than hiding the problem silently (ADR-003 cross-cutting).
    /// </summary>
    public bool IsStale { get; set; }

    /// <summary>
    /// Computed: units available for new reservations or sales.
    /// Not persisted — calculated on read.
    /// </summary>
    public int AvailableQuantity => StockQuantity - ReservedQuantity;
}
