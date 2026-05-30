using Nop.Data;
using Nop.Core.Domain.Inventory;
using Nop.Core.Domain.Catalog;  // ← ADD THIS

namespace Nop.Services.Inventory;

/// <summary>
/// Manages the VerdeMart cross-channel stock ledger. All reads and writes
/// to StockLedgerEntry go through this service — no other code touches
/// the repository directly.
/// </summary>
public partial interface IStockLedgerService
{
    /// <summary>
    /// Returns the ledger entry for a product/warehouse pair, or null if
    /// no entry exists yet (product not yet tracked by the WMS).
    /// </summary>
    Task<StockLedgerEntry?> GetEntryAsync(int productId, int warehouseId = 0);

    /// <summary>
    /// Creates or updates the stock ledger from an inbound WMS event.
    /// Decrements StockQuantity by <paramref name="quantityPicked"/> and
    /// updates LastUpdatedAtUtc. Used by WmsStockPickedConsumer.
    /// </summary>
    Task ApplyPickAsync(int productId, int warehouseId, int quantityPicked, DateTime eventUtc);

    /// <summary>
    /// Atomically reserves <paramref name="quantity"/> units for a given
    /// product. Returns the reservation id on success, or null if there
    /// is insufficient available stock (QAS-2).
    /// </summary>
    Task<Guid?> TryReserveAsync(int productId, int warehouseId, int quantity, Guid idempotencyKey);

    /// <summary>
    /// Releases a previously created reservation. Called when checkout
    /// is abandoned or an order is cancelled.
    /// </summary>
    Task ReleaseReservationAsync(int productId, int warehouseId, int quantity);

    /// <summary>
    /// Marks all entries whose LastUpdatedAtUtc is older than
    /// <paramref name="threshold"/> as stale. Called by StalenessCheckerTask.
    /// </summary>
    Task MarkStaleEntriesAsync(TimeSpan threshold);

    /// <summary>
    /// Returns all entries currently marked as stale. Used by the Ops Dashboard.
    /// </summary>
    Task<IList<StockLedgerEntry>> GetStaleEntriesAsync();

    /// <summary>
    /// Gets a product by ID for the Ops Dashboard.
    /// </summary>
    Task<Product> GetProductByIdAsync(int productId);  // ← ADD THIS

    /// <summary>Persists a new reservation record after TryReserveAsync succeeds.</summary>
    Task StoreReservationAsync(StockReservation reservation);

    /// <summary>Returns an active reservation by its business key, or null.</summary>
    Task<StockReservation?> GetReservationAsync(Guid reservationId);

    /// <summary>Deletes the reservation record. Called after ReleaseReservationAsync.</summary>
    Task RemoveReservationAsync(Guid reservationId);

    /// <summary>
    /// Gets the repository for direct access
    /// </summary>
}