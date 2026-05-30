namespace Nop.Core.Domain.Inventory;

/// <summary>
/// Tracks an active cross-channel stock reservation.
/// Created by POST /api/inventory/reservations and removed on DELETE.
/// </summary>
public partial class StockReservation : BaseEntity
{
    public Guid ReservationId { get; set; }
    public int ProductId { get; set; }
    public int WarehouseId { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
