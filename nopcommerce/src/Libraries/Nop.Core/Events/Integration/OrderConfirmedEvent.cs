namespace Nop.Core.Events.Integration;

/// <summary>
/// Integration event emitted when an order is successfully placed in
/// nopCommerce. This is the wire format delivered to surrounding
/// systems (ERP, WMS) via RabbitMQ — kept deliberately minimal and
/// stable to insulate downstream consumers from internal entity churn.
/// </summary>
public sealed class OrderConfirmedEvent
{
    public Guid EventId { get; init; }
    public int OrderId { get; init; }
    public Guid OrderGuid { get; init; }
    public int CustomerId { get; init; }
    public int StoreId { get; init; }
    public decimal OrderTotal { get; init; }
    public string CurrencyCode { get; init; }
    public DateTime CreatedOnUtc { get; init; }
    public OrderConfirmedLine[] Lines { get; init; }

    public sealed class OrderConfirmedLine
    {
        public int ProductId { get; init; }
        public int Quantity { get; init; }
        public decimal UnitPrice { get; init; }
    }
}
