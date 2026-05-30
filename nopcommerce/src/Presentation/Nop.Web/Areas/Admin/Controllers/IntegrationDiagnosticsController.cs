using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Core.Domain.Integration;
using Nop.Core.Domain.Inventory;
using Nop.Data;
using Nop.Services.Integration.Messaging;
using Nop.Services.Inventory;

namespace Nop.Web.Areas.Admin.Controllers;

/// <summary>
/// VerdeMart integration diagnostics and Phase 5 test-support endpoints.
///
/// Phase 1: PublishTestEvent — fire-and-forget sanity ping to verify RabbitMQ pipeline.
/// Phase 5: SeedStockLedger, GetStock, GetOutboxDepth — called by QAS scripts so
///          they don't need raw SQL / direct DB access.
/// </summary>
public partial class IntegrationDiagnosticsController : BaseAdminController
{
    protected readonly IRabbitMqPublisher _publisher;
    protected readonly IStockLedgerService _stockLedgerService;
    protected readonly IRepository<OutboxMessage> _outboxRepository;
    protected readonly IRepository<StockLedgerEntry> _ledgerRepository;

    public IntegrationDiagnosticsController(
        IRabbitMqPublisher publisher,
        IStockLedgerService stockLedgerService,
        IRepository<OutboxMessage> outboxRepository,
        IRepository<StockLedgerEntry> ledgerRepository)
    {
        _publisher = publisher;
        _stockLedgerService = stockLedgerService;
        _outboxRepository = outboxRepository;
        _ledgerRepository = ledgerRepository;
    }

    // ── Phase 1 — sanity ping ─────────────────────────────────────────────

    /// <summary>
    /// Fire-and-forget publish so the operator can verify the RabbitMQ
    /// pipeline end-to-end in the management UI.
    /// POST /Admin/IntegrationDiagnostics/PublishTestEvent
    /// </summary>
    [HttpPost]
    public virtual async Task<IActionResult> PublishTestEvent()
    {
        var eventId = Guid.NewGuid();
        var payload = JsonConvert.SerializeObject(new
        {
            eventId,
            type = "verdemart.diagnostics.ping",
            emittedAtUtc = DateTime.UtcNow,
            note = "Phase 1 sanity check"
        });

        await _publisher.PublishAsync(
            routingKey: "verdemart.diagnostics.ping",
            payload: payload,
            eventId: eventId);

        return Json(new { status = "published", eventId, routingKey = "verdemart.diagnostics.ping" });
    }

    // ── Phase 5 — test-support endpoints ─────────────────────────────────

    /// <summary>
    /// Seeds a StockLedgerEntry for the given product/warehouse.
    /// Creates a new row or resets an existing one to the requested quantity.
    /// Called by QAS scripts before each test run so they start from a known state.
    /// POST /Admin/IntegrationDiagnostics/SeedStockLedger
    /// Body: { "productId": 1, "warehouseId": 0, "stockQuantity": 10 }
    /// </summary>
    [HttpPost]
    public virtual async Task<IActionResult> SeedStockLedger([FromBody] SeedStockRequest request)
    {
        if (request.ProductId <= 0)
            return BadRequest(new { error = "productId must be > 0" });
        if (request.StockQuantity < 0)
            return BadRequest(new { error = "stockQuantity must be >= 0" });

        var existing = await _stockLedgerService.GetEntryAsync(request.ProductId, request.WarehouseId);

        if (existing == null)
        {
            await _ledgerRepository.InsertAsync(new StockLedgerEntry
            {
                ProductId        = request.ProductId,
                WarehouseId      = request.WarehouseId,
                StockQuantity    = request.StockQuantity,
                ReservedQuantity = 0,
                LastUpdatedAtUtc = DateTime.UtcNow,
                IsStale          = false
            }, publishEvent: false);
        }
        else
        {
            existing.StockQuantity    = request.StockQuantity;
            existing.ReservedQuantity = 0;
            existing.LastUpdatedAtUtc = DateTime.UtcNow;
            existing.IsStale          = false;
            await _ledgerRepository.UpdateAsync(existing, publishEvent: false);
        }

        return Json(new
        {
            status        = "seeded",
            productId     = request.ProductId,
            warehouseId   = request.WarehouseId,
            stockQuantity = request.StockQuantity
        });
    }

    /// <summary>
    /// Returns the current StockLedgerEntry for a product/warehouse.
    /// Used by QAS-2 and QAS-5 scripts to read stock without DB access.
    /// GET /Admin/IntegrationDiagnostics/GetStock?productId=1&amp;warehouseId=0
    /// </summary>
    [HttpGet]
    public virtual async Task<IActionResult> GetStock([FromQuery] int productId, [FromQuery] int warehouseId = 0)
    {
        var entry = await _stockLedgerService.GetEntryAsync(productId, warehouseId);
        if (entry == null)
            return NotFound(new { error = $"No ledger entry for productId={productId} warehouseId={warehouseId}" });

        return Json(new
        {
            productId         = entry.ProductId,
            warehouseId       = entry.WarehouseId,
            stockQuantity     = entry.StockQuantity,
            reservedQuantity  = entry.ReservedQuantity,
            availableQuantity = entry.AvailableQuantity,
            lastUpdatedAtUtc  = entry.LastUpdatedAtUtc,
            isStale           = entry.IsStale
        });
    }

    /// <summary>
    /// Returns unpublished outbox message counts grouped by EventType.
    /// Used by QAS-1 to verify messages accumulated during ERP outage,
    /// and to poll until the outbox drains after recovery.
    /// GET /Admin/IntegrationDiagnostics/GetOutboxDepth
    /// </summary>
    [HttpGet]
    public virtual IActionResult GetOutboxDepth()
    {
        var unpublished = _outboxRepository.Table
            .Where(m => m.PublishedOnUtc == null)
            .ToList();

        var rows = unpublished
            .GroupBy(m => m.EventType)
            .Select(g => new
            {
                eventType        = g.Key,
                count            = g.Count(),
                oldestCreatedUtc = g.Min(m => m.CreatedOnUtc)
            })
            .OrderByDescending(r => r.count)
            .ToList();

        return Json(new
        {
            totalPending     = unpublished.Count,
            rows,
            oldestCreatedUtc = unpublished.Any() ? unpublished.Min(m => m.CreatedOnUtc) : (DateTime?)null
        });
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────

public class SeedStockRequest
{
    public int ProductId     { get; set; }
    public int WarehouseId   { get; set; } = 0;
    public int StockQuantity { get; set; } = 10;
}