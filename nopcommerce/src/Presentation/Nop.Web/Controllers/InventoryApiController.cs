using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Inventory;
using Nop.Services.Inventory;

namespace Nop.Web.Controllers;

/// <summary>
/// Internal REST API for cross-channel stock reservation (QAS-2).
/// Called by POS and external channels — not by browser clients.
/// </summary>
[ApiController]
[Route("api/inventory")]
public class InventoryApiController : ControllerBase
{
    protected readonly IStockLedgerService _stockLedgerService;

    public InventoryApiController(IStockLedgerService stockLedgerService)
    {
        _stockLedgerService = stockLedgerService;
    }

    /// <summary>
    /// Reserves stock for a given product/warehouse. Idempotent: if the
    /// same reservationId is sent again, returns 200 without double-booking.
    /// Returns 409 when available stock is insufficient.
    /// </summary>
    [HttpPost("reservations")]
    public async Task<IActionResult> CreateReservation([FromBody] CreateReservationRequest request)
    {
        // Idempotency: if reservation already exists, return success.
        var existing = await _stockLedgerService.GetReservationAsync(request.ReservationId);
        if (existing is not null)
            return Ok(new { reservationId = existing.ReservationId });

        var reservationId = await _stockLedgerService.TryReserveAsync(
            request.ProductId, request.WarehouseId, request.Quantity, request.ReservationId);

        if (reservationId is null)
            return Conflict(new { error = "Insufficient available stock." });

        await _stockLedgerService.StoreReservationAsync(new StockReservation
        {
            ReservationId = reservationId.Value,
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            Quantity = request.Quantity,
            CreatedAtUtc = DateTime.UtcNow
        });

        return Ok(new { reservationId = reservationId.Value });
    }

    /// <summary>
    /// Releases a reservation and frees the reserved stock.
    /// Returns 204 on success, 404 if the reservation does not exist.
    /// </summary>
    [HttpDelete("reservations/{reservationId:guid}")]
    public async Task<IActionResult> DeleteReservation(Guid reservationId)
    {
        var reservation = await _stockLedgerService.GetReservationAsync(reservationId);
        if (reservation is null)
            return NotFound(new { error = $"Reservation {reservationId} not found." });

        await _stockLedgerService.ReleaseReservationAsync(
            reservation.ProductId, reservation.WarehouseId, reservation.Quantity);

        await _stockLedgerService.RemoveReservationAsync(reservationId);

        return NoContent();
    }
}

public class CreateReservationRequest
{
    public Guid ReservationId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int Quantity { get; set; }
}
