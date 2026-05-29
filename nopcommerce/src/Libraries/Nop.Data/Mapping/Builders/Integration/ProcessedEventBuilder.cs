using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Integration;

namespace Nop.Data.Mapping.Builders.Integration;

/// <summary>
/// LinqToDb / FluentMigrator schema builder for <see cref="ProcessedEvent"/>.
/// The unique index on EventId is the enforcement point for idempotency (ADR-005).
/// </summary>
public partial class ProcessedEventBuilder : NopEntityBuilder<ProcessedEvent>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(ProcessedEvent.EventId)).AsGuid().NotNullable().Unique()
            .WithColumn(nameof(ProcessedEvent.ProcessedAtUtc)).AsDateTime().NotNullable();
    }
}
