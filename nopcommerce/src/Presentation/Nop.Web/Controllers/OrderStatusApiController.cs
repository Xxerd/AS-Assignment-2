using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Data;
using Nop.Services.Customers;
using Nop.Services.Orders;
using Nop.Web.Models.Order;

namespace Nop.Web.Controllers;

/// <summary>
/// API for cross-channel order status visibility (QAS-4).
/// Used by POS terminal to look up online orders.
/// GET /api/orders/{id}/status
/// GET /api/orders/search?email=xxx
///
/// FIX (QAS-4): The QAS script polls for `shipment.trackingNumber` in the
/// response. The previous response embedded shipments as an array; the
/// polling script reads `d.get('shipment',{}).get('trackingNumber','')`.
/// Response now includes a top-level `shipment` object with the most
/// recently updated shipment, in addition to the full `shipments` array.
/// </summary>
[ApiController]
[Route("api/orders")]
public class OrderStatusApiController : ControllerBase
{
    protected readonly IRepository<Order> _orderRepository;
    protected readonly IRepository<Shipment> _shipmentRepository;
    protected readonly IOrderService _orderService;
    protected readonly ICustomerService _customerService;

    public OrderStatusApiController(
        IRepository<Order> orderRepository,
        IRepository<Shipment> shipmentRepository,
        IOrderService orderService,
        ICustomerService customerService)
    {
        _orderRepository = orderRepository;
        _shipmentRepository = shipmentRepository;
        _orderService = orderService;
        _customerService = customerService;
    }

    /// <summary>
    /// Get order status by order ID.
    /// Returns consolidated cross-channel order state.
    /// </summary>
    [HttpGet("{id:int}/status")]
    public async Task<IActionResult> GetOrderStatus(int id)
    {
        var order = await _orderRepository.GetByIdAsync(id);
        if (order == null)
            return NotFound(new { error = $"Order {id} not found." });

        var shipments = await _shipmentRepository.Table
            .Where(s => s.OrderId == order.Id)
            .ToListAsync();

        var customer = await _customerService.GetCustomerByIdAsync(order.CustomerId);

        // Most recently updated shipment — this is what QAS-4 polls for.
        var latestShipment = shipments
            .OrderByDescending(s => s.ShippedDateUtc ?? DateTime.MinValue)
            .FirstOrDefault();

        var shipmentModels = shipments.Select(s => new ShipmentTrackingModel
        {
            ShipmentId       = s.Id,
            TrackingNumber   = s.TrackingNumber ?? string.Empty,
            ShippedDateUtc   = s.ShippedDateUtc,
            DeliveredDateUtc = s.DeliveryDateUtc,
            Status           = GetShipmentStatus(s),
            Carrier          = "Unknown"
        }).ToList();

        return Ok(new
        {
            orderId        = order.Id,
            orderGuid      = order.OrderGuid,
            orderStatus    = order.OrderStatus.ToString(),
            paymentStatus  = order.PaymentStatus.ToString(),
            shippingStatus = order.ShippingStatus.ToString(),
            createdOnUtc   = order.CreatedOnUtc,
            paidOnUtc      = order.PaidDateUtc,
            shippedOnUtc   = shipments.Where(s => s.ShippedDateUtc.HasValue).Select(s => s.ShippedDateUtc).Min(),
            deliveredOnUtc = shipments.Where(s => s.DeliveryDateUtc.HasValue).Select(s => s.DeliveryDateUtc).Max(),
            customerEmail  = customer?.Email ?? string.Empty,
            customerName   = customer != null ? $"{customer.FirstName} {customer.LastName}".Trim() : string.Empty,
            channel        = "web",
            // Top-level `shipment` object: QAS-4 script reads d.get('shipment',{}).get('trackingNumber','')
            shipment = latestShipment == null ? null : new
            {
                shipmentId     = latestShipment.Id,
                trackingNumber = latestShipment.TrackingNumber ?? string.Empty,
                status         = GetShipmentStatus(latestShipment),
                shippedDateUtc = latestShipment.ShippedDateUtc,
                deliveredDateUtc = latestShipment.DeliveryDateUtc
            },
            shipments = shipmentModels
        });
    }

    /// <summary>Get order status by order GUID.</summary>
    [HttpGet("{guid:guid}/status")]
    public async Task<IActionResult> GetOrderStatusByGuid(Guid guid)
    {
        var order = await _orderRepository.Table
            .FirstOrDefaultAsync(o => o.OrderGuid == guid);

        if (order == null)
            return NotFound(new { error = $"Order with GUID {guid} not found." });

        return await GetOrderStatus(order.Id);
    }

    /// <summary>Search orders by email (for POS clerk lookup).</summary>
    [HttpGet("search")]
    public async Task<IActionResult> SearchOrders([FromQuery] string email, [FromQuery] int limit = 10)
    {
        if (string.IsNullOrEmpty(email))
            return BadRequest(new { error = "Email parameter is required" });

        var customer = await _customerService.GetCustomerByEmailAsync(email);
        if (customer == null)
            return Ok(new { orders = new List<object>() });

        var orders = await _orderRepository.Table
            .Where(o => o.CustomerId == customer.Id)
            .OrderByDescending(o => o.CreatedOnUtc)
            .Take(limit)
            .Select(o => new { o.Id, o.OrderGuid, o.OrderTotal, o.CreatedOnUtc, o.OrderStatus })
            .ToListAsync();

        return Ok(new { orders });
    }

    private static string GetShipmentStatus(Shipment shipment)
    {
        if (shipment.DeliveryDateUtc.HasValue) return "Delivered";
        if (shipment.ShippedDateUtc.HasValue) return "Shipped";
        return "Processing";
    }
}