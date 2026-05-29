namespace Nop.Core.Domain.Integration;

/// <summary>
/// Inbound event published by the WMS (OpenBoxes) when a pick is completed.
/// Routing key: wms.stock.picked  Exchange: commerce.events
/// </summary>
public class StockPickedEvent
{
    public Guid EventId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int QuantityPicked { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}
