using FluentMigrator;
using Nop.Core.Domain.ScheduleTasks;

namespace Nop.Data.Migrations.VerdeMart;

/// <summary>
/// VerdeMart Phase 3 — registers the CarrierRateRefreshTask in the
/// nopCommerce scheduler so the carrier circuit breaker can recover.
///
/// FIX (QAS-3): Without this migration the task never runs and the Polly
/// circuit breaker stays Open permanently after a fault, because nobody
/// ever makes a probe request.
///
/// Runs every 20 seconds so the probe fires well within the 30-second
/// BreakDuration. Once the WireMock fault is cleared, the next task
/// execution calls GetRateAsync, Polly transitions to HalfOpen, the
/// probe succeeds, and the circuit closes.
/// </summary>
[NopUpdateMigration("2026-05-30 12:00:00", "5.00", UpdateMigrationType.Data)]
public class CarrierScheduleTaskMigration : Migration
{
    protected readonly INopDataProvider _dataProvider;

    public CarrierScheduleTaskMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        const string taskType = "Nop.Services.Carriers.Tasks.CarrierRateRefreshTask, Nop.Services";

        // Guard: idempotent — only insert if not already present
        if (_dataProvider.GetTable<ScheduleTask>().Any(st => st.Type == taskType))
            return;

        _dataProvider.InsertEntity(new ScheduleTask
        {
            Name           = "VerdeMart: carrier rate refresh (circuit probe)",
            // 20s: fires within the 30s BreakDuration, giving the circuit time to recover
            Seconds        = 20,
            Type           = taskType,
            Enabled        = true,
            LastEnabledUtc = DateTime.UtcNow,
            StopOnError    = false
        });
    }

    public override void Down()
    {
    }
}