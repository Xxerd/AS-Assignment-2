using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Integration;

namespace Nop.Data.Mapping.Builders.Integration;

/// <summary>
/// LinqToDb / FluentMigrator builder for <see cref="OutboxMessage"/>.
/// </summary>
public partial class OutboxMessageBuilder : NopEntityBuilder<OutboxMessage>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(OutboxMessage.EventId)).AsGuid().NotNullable().Unique()
            .WithColumn(nameof(OutboxMessage.EventType)).AsString(256).NotNullable()
            .WithColumn(nameof(OutboxMessage.Payload)).AsString(int.MaxValue).NotNullable()
            .WithColumn(nameof(OutboxMessage.CreatedOnUtc)).AsDateTime().NotNullable()
            .WithColumn(nameof(OutboxMessage.PublishedOnUtc)).AsDateTime().Nullable()
            .WithColumn(nameof(OutboxMessage.Attempts)).AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn(nameof(OutboxMessage.LastError)).AsString(int.MaxValue).Nullable();
    }
}
