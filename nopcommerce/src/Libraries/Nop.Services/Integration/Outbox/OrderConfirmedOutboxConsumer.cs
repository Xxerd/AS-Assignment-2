using Nop.Core.Domain.Orders;
using Nop.Core.Events.Integration;
using Nop.Services.Events;
using Nop.Services.Orders;

namespace Nop.Services.Integration.Outbox;

/// <summary>
/// Listens to nopCommerce's in-process <see cref="OrderPlacedEvent"/>
/// and writes the corresponding integration event
/// (<see cref="OrderConfirmedEvent"/>) to the outbox.
///
/// Hooking on the in-process event keeps the integration code out of
/// <c>OrderProcessingService</c>: any change to how orders are placed
/// continues to flow through the same event without further edits.
/// </summary>
public partial class OrderConfirmedOutboxConsumer : IConsumer<OrderPlacedEvent>
{
    protected readonly IOutboxWriter _outboxWriter;
    protected readonly IOrderService _orderService;

    public OrderConfirmedOutboxConsumer(IOutboxWriter outboxWriter, IOrderService orderService)
    {
        _outboxWriter = outboxWriter;
        _orderService = orderService;
    }

    public virtual async Task HandleEventAsync(OrderPlacedEvent eventMessage)
    {
        var order = eventMessage.Order;
        if (order is null)
            return;

        var items = await _orderService.GetOrderItemsAsync(order.Id);

        var integrationEvent = new OrderConfirmedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = order.Id,
            OrderGuid = order.OrderGuid,
            CustomerId = order.CustomerId,
            StoreId = order.StoreId,
            OrderTotal = order.OrderTotal,
            CurrencyCode = order.CustomerCurrencyCode,
            CreatedOnUtc = order.CreatedOnUtc,
            Lines = items.Select(i => new OrderConfirmedEvent.OrderConfirmedLine
            {
                ProductId = i.ProductId,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPriceExclTax
            }).ToArray()
        };

        await _outboxWriter.EnqueueAsync(integrationEvent);
    }
}
