using FluentMigrator;
using Nop.Core.Domain.ScheduleTasks;

namespace Nop.Data.Migrations.VerdeMart;

/// <summary>
/// VerdeMart Phase 3 — registers the StalenessCheckerTask in the
/// nopCommerce scheduler. Runs every 60 seconds.
///
/// Uses NopUpdateMigration with UpdateMigrationType.Data, matching the
/// pattern in OutboxPublisherScheduleTaskMigration exactly so the runner
/// picks it up on first boot.
/// </summary>
[NopUpdateMigration("2026-05-29 10:00:02", "5.00", UpdateMigrationType.Data)]
public class InventoryScheduleTasksMigration : Migration
{
    protected readonly INopDataProvider _dataProvider;

    public InventoryScheduleTasksMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        const string stalenessTaskType = "Nop.Services.Inventory.StalenessCheckerTask, Nop.Services";

        // Guard: only insert if not already present (idempotent)
        if (_dataProvider.GetTable<ScheduleTask>().Any(st => st.Type == stalenessTaskType))
            return;

        _dataProvider.InsertEntity(new ScheduleTask
        {
            Name = "VerdeMart: staleness checker",
            Seconds = 60,
            Type = stalenessTaskType,
            Enabled = true,
            LastEnabledUtc = DateTime.UtcNow,
            StopOnError = false
        });
    }

    public override void Down()
    {
    }
}