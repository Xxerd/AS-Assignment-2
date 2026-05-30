using FluentMigrator;
using Nop.Core.Domain.Inventory;
using Nop.Data.Extensions;

namespace Nop.Data.Migrations.VerdeMart;

/// <summary>
/// Creates the StockReservation table.
/// MigrationProcessType.NoMatter ensures this runs on both fresh installs
/// and on subsequent startups for existing installations (via AppStartedConsumer).
/// CreateTableIfNotExists makes it idempotent.
/// </summary>
[NopMigration("2026-05-30 10:00:00", "VerdeMart: StockReservation table", MigrationProcessType.NoMatter)]
public class ReservationSchemaMigration : ForwardOnlyMigration
{
    public override void Up()
    {
        this.CreateTableIfNotExists<StockReservation>();
    }
}
