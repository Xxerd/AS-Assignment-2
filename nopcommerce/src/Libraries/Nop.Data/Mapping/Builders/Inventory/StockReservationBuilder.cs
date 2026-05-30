using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Inventory;

namespace Nop.Data.Mapping.Builders.Inventory;

public partial class StockReservationBuilder : NopEntityBuilder<StockReservation>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(StockReservation.ReservationId)).AsGuid().NotNullable().Unique()
            .WithColumn(nameof(StockReservation.ProductId)).AsInt32().NotNullable()
            .WithColumn(nameof(StockReservation.WarehouseId)).AsInt32().NotNullable()
            .WithColumn(nameof(StockReservation.Quantity)).AsInt32().NotNullable()
            .WithColumn(nameof(StockReservation.CreatedAtUtc)).AsDateTime().NotNullable();
    }
}
