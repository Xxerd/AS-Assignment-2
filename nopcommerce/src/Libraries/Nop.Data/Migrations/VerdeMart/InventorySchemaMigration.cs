using FluentMigrator;
using Nop.Core.Domain.Integration;
using Nop.Core.Domain.Inventory;
using Nop.Data.Extensions;

namespace Nop.Data.Migrations.VerdeMart;

/// <summary>
/// VerdeMart Phase 3 — creates the cross-channel inventory tables:
///
/// StockLedgerEntry: the Commerce Core's replica of WMS physical stock.
///   Updated by WmsStockPickedConsumer on inbound events from OpenBoxes.
///   Read by the Reservation API and the storefront stock display.
///
/// ProcessedEvent: deduplication table for inbound integration events
///   (ADR-005 idempotent consumers). A unique index on EventId ensures
///   the same WMS event can never be applied twice, even if RabbitMQ
///   redelivers it after a consumer crash.
/// </summary>
[NopSchemaMigration("2026-05-29 10:00:00", "VerdeMart: StockLedgerEntry and ProcessedEvent tables", MigrationProcessType.Installation)]
public class InventorySchemaMigration : ForwardOnlyMigration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<StockLedgerEntry>();
        this.CreateTableIfNotExists<ProcessedEvent>();
    }
}
