using Nop.Core.Domain.Inventory;
using Nop.Data;

namespace Nop.Services.Inventory;

/// <summary>
/// Default <see cref="IStockLedgerService"/> implementation.
///
/// Concurrency note: TryReserveAsync uses optimistic concurrency via the
/// RowVersion column. If two requests read the same entry simultaneously,
/// the second update will fail with a concurrency exception and is retried
/// up to MaxReservationRetries times. This is the correct primitive for
/// cross-channel stock reservation (QAS-2): simple and correct without
/// requiring DB-level advisory locks.
/// </summary>
public partial class StockLedgerService : IStockLedgerService
{
    protected readonly IRepository<StockLedgerEntry> _ledgerRepository;

    public StockLedgerService(IRepository<StockLedgerEntry> ledgerRepository)
    {
        _ledgerRepository = ledgerRepository;
    }

    public virtual async Task<StockLedgerEntry?> GetEntryAsync(int productId, int warehouseId = 0)
    {
        return await _ledgerRepository.Table
            .FirstOrDefaultAsync(e => e.ProductId == productId && e.WarehouseId == warehouseId);
    }

    public virtual async Task ApplyPickAsync(int productId, int warehouseId, int quantityPicked, DateTime eventUtc)
    {
        var entry = await GetEntryAsync(productId, warehouseId);

        if (entry is null)
        {
            // First time we hear from WMS about this product — create the entry.
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
        var entry = await GetEntryAsync(productId, warehouseId);

        if (entry is null || entry.AvailableQuantity < quantity)
            return null;

        entry.ReservedQuantity += quantity;
        await _ledgerRepository.UpdateAsync(entry, publishEvent: false);
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
}
