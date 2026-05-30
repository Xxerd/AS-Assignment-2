using LinqToDB;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Inventory;
using Nop.Data;

namespace Nop.Services.Inventory;

public partial class StockLedgerService : IStockLedgerService
{
    protected readonly IRepository<StockLedgerEntry> _ledgerRepository;
    protected readonly IRepository<StockReservation> _reservationRepository;
    protected readonly IRepository<Product> _productRepository;

    public StockLedgerService(
        IRepository<StockLedgerEntry> ledgerRepository,
        IRepository<StockReservation> reservationRepository,
        IRepository<Product> productRepository)
    {
        _ledgerRepository = ledgerRepository;
        _reservationRepository = reservationRepository;
        _productRepository = productRepository;
    }

    public virtual async Task<StockLedgerEntry?> GetEntryAsync(int productId, int warehouseId = 0)
    {
        var entry = await _ledgerRepository.Table
            .FirstOrDefaultAsync(e => e.ProductId == productId && e.WarehouseId == warehouseId);
        
        // Phase 3.1: Auto-create missing StockLedgerEntry (Lazy Creation)
        if (entry == null)
        {
            var product = await _productRepository.GetByIdAsync(productId);
            if (product != null && product.ManageInventoryMethod == ManageInventoryMethod.ManageStock)
            {
                entry = new StockLedgerEntry
                {
                    ProductId = productId,
                    WarehouseId = warehouseId,
                    StockQuantity = product.StockQuantity,
                    ReservedQuantity = 0,
                    LastUpdatedAtUtc = DateTime.UtcNow,
                    IsStale = false
                };
                await _ledgerRepository.InsertAsync(entry, publishEvent: false);
            }
        }
        
        return entry;
    }

    public virtual async Task<Product> GetProductByIdAsync(int productId)
    {
        return await _productRepository.GetByIdAsync(productId);
    }

    public virtual async Task ApplyPickAsync(int productId, int warehouseId, int quantityPicked, DateTime eventUtc)
    {
        var entry = await GetEntryAsync(productId, warehouseId);

        if (entry is null)
        {
            entry = new StockLedgerEntry
            {
                ProductId = productId,
                WarehouseId = warehouseId,
                StockQuantity = Math.Max(0, -quantityPicked),
                ReservedQuantity = 0,
                LastUpdatedAtUtc = eventUtc,
                IsStale = false
            };
            await _ledgerRepository.InsertAsync(entry, publishEvent: false);
            return;
        }

        entry.StockQuantity = Math.Max(0, entry.StockQuantity - quantityPicked);
        entry.LastUpdatedAtUtc = eventUtc;
        entry.IsStale = false;

        await _ledgerRepository.UpdateAsync(entry, publishEvent: false);
    }

    public virtual async Task<Guid?> TryReserveAsync(int productId, int warehouseId, int quantity, Guid idempotencyKey)
    {
        // QAS-2 race safety: use an atomic conditional UPDATE so that
        // concurrent requests cannot both pass the availability check.
        // The UPDATE only touches rows where the available quantity is
        // sufficient; if zero rows are affected another request already
        // consumed the stock and we return null (409 at the API layer).
        //
        // Equivalent SQL:
        //   UPDATE StockLedgerEntry
        //   SET    ReservedQuantity = ReservedQuantity + @qty
        //   WHERE  ProductId = @pid AND WarehouseId = @wid
        //          AND (StockQuantity - ReservedQuantity) >= @qty
        //
        // LinqToDb executes this as a single round-trip with no
        // application-level read in between, so there is no TOCTOU window.

        var rowsAffected = await _ledgerRepository.Table
            .Where(e => e.ProductId == productId
                     && e.WarehouseId == warehouseId
                     && (e.StockQuantity - e.ReservedQuantity) >= quantity)
            .Set(e => e.ReservedQuantity, e => e.ReservedQuantity + quantity)
            .UpdateAsync();

        if (rowsAffected == 0)
            return null;

        return idempotencyKey;
    }

    public virtual async Task ReleaseReservationAsync(int productId, int warehouseId, int quantity)
    {
        var entry = await GetEntryAsync(productId, warehouseId);
        if (entry is null)
            return;

        entry.ReservedQuantity = Math.Max(0, entry.ReservedQuantity - quantity);
        await _ledgerRepository.UpdateAsync(entry, publishEvent: false);
    }

    public virtual async Task MarkStaleEntriesAsync(TimeSpan threshold)
    {
        var cutoff = DateTime.UtcNow - threshold;

        var stale = _ledgerRepository.Table
            .Where(e => !e.IsStale && e.LastUpdatedAtUtc < cutoff)
            .ToList();

        foreach (var entry in stale)
        {
            entry.IsStale = true;
            await _ledgerRepository.UpdateAsync(entry, publishEvent: false);
        }
    }

    public virtual async Task<IList<StockLedgerEntry>> GetStaleEntriesAsync()
    {
        return await _ledgerRepository.Table
            .Where(e => e.IsStale)
            .ToListAsync();
    }

    public virtual async Task StoreReservationAsync(StockReservation reservation)
    {
        await _reservationRepository.InsertAsync(reservation, publishEvent: false);
    }

    public virtual async Task<StockReservation?> GetReservationAsync(Guid reservationId)
    {
        return await _reservationRepository.Table
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);
    }

    public virtual async Task RemoveReservationAsync(Guid reservationId)
    {
        var reservation = await GetReservationAsync(reservationId);
        if (reservation is not null)
            await _reservationRepository.DeleteAsync(reservation, publishEvent: false);
    }
}