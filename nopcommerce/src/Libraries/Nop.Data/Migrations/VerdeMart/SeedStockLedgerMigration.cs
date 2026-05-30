using FluentMigrator;

namespace Nop.Data.Migrations.VerdeMart;

/// <summary>
/// Phase 3.1 — Back-fill StockLedgerEntry from existing Product.StockQuantity
/// This migration runs after InventorySchemaMigration and populates the
/// stock ledger with initial data from products that already exist.
/// </summary>
[NopSchemaMigration("2026-05-30 17:00:00", "VerdeMart: Seed StockLedgerEntry from products", MigrationProcessType.Installation)]
public class SeedStockLedgerMigration : ForwardOnlyMigration
{
    public override void Up()
    {
        Execute.Sql(@"
            -- Back-fill StockLedgerEntry from existing products that track inventory
            INSERT INTO StockLedgerEntry (ProductId, WarehouseId, StockQuantity, ReservedQuantity, LastUpdatedAtUtc, IsStale)
            SELECT 
                p.Id, 
                0,                           -- Default warehouse
                p.StockQuantity,              -- Current stock from product
                0,                           -- No reservations initially
                GETUTCDATE(),                -- Current time (stock is fresh at install)
                0                            -- Not stale initially
            FROM Product p
            WHERE p.ManageInventoryMethodId = 1  -- Track inventory
              AND NOT EXISTS (
                  SELECT 1 FROM StockLedgerEntry s WHERE s.ProductId = p.Id
              );
        ");
    }
}
