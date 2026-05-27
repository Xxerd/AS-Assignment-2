using System;
using System.Linq;
using FluentMigrator;
using Nop.Core.Domain.ScheduleTasks;

namespace Nop.Data.Migrations.VerdeMart;

/// <summary>
/// Inserts the ScheduleTask row that drives <see cref="OutboxPublisherTask"/>.
/// Run as a data migration after the schema migration above.
/// </summary>
[NopUpdateMigration("2026-05-15 00:00:02", "5.00", UpdateMigrationType.Data)]
public class OutboxPublisherScheduleTaskMigration : Migration
{
    protected readonly INopDataProvider _dataProvider;

    public OutboxPublisherScheduleTaskMigration(INopDataProvider dataProvider)
    {
        _dataProvider = dataProvider;
    }

    public override void Up()
    {
        const string taskType = "Nop.Services.Integration.Outbox.OutboxPublisherTask, Nop.Services";

        if (_dataProvider.GetTable<ScheduleTask>().Any(st => st.Type == taskType))
            return;

        _dataProvider.InsertEntity(new ScheduleTask
        {
            Name = "VerdeMart: outbox publisher",
            Seconds = 30,
            Type = taskType,
            Enabled = true,
            LastEnabledUtc = DateTime.UtcNow,
            StopOnError = false
        });
    }

    public override void Down()
    {
    }
}
