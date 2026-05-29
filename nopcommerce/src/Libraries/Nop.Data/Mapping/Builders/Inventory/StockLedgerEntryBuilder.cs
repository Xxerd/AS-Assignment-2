using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Inventory;

namespace Nop.Data.Mapping.Builders.Inventory;

/// <summary>
/// LinqToDb / FluentMigrator schema builder for <see cref="StockLedgerEntry"/>.
/// </summary>
public partial class StockLedgerEntryBuilder : NopEntityBuilder<StockLedgerEntry>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(StockLedgerEntry.ProductId)).AsInt32().NotNullable()
            .WithColumn(nameof(StockLedgerEntry.WarehouseId)).AsInt32().NotNullable()
            .WithColumn(nameof(StockLedgerEntry.StockQuantity)).AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn(nameof(StockLedgerEntry.ReservedQuantity)).AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn(nameof(StockLedgerEntry.LastUpdatedAtUtc)).AsDateTime().NotNullable()
            .WithColumn(nameof(StockLedgerEntry.IsStale)).AsBoolean().NotNullable().WithDefaultValue(false);
    }
}
