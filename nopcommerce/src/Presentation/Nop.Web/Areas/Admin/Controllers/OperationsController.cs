using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Integration;
using Nop.Data;
using Nop.Services.Carriers;
using Nop.Services.Inventory;
using Nop.Services.Security;
using Nop.Web.Areas.Admin.Models.Operations;
using Nop.Web.Framework;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Mvc.Filters;

namespace Nop.Web.Areas.Admin.Controllers;

[AuthorizeAdmin]
[Area(AreaNames.ADMIN)]
public partial class OperationsController : BaseAdminController
{
    protected readonly IStockLedgerService _stockLedgerService;
    protected readonly IRepository<OutboxMessage> _outboxRepository;
    protected readonly ICircuitBreakerStateMonitor _circuitMonitor;
    protected readonly IPermissionService _permissionService;

    public OperationsController(
        IStockLedgerService stockLedgerService,
        IRepository<OutboxMessage> outboxRepository,
        ICircuitBreakerStateMonitor circuitMonitor,
        IPermissionService permissionService)
    {
        _stockLedgerService = stockLedgerService;
        _outboxRepository = outboxRepository;
        _circuitMonitor = circuitMonitor;
        _permissionService = permissionService;
    }

    public virtual async Task<IActionResult> Index()
    {
        if (!await _permissionService.AuthorizeAsync(StandardPermission.System.MANAGE_MAINTENANCE))
            return AccessDeniedView();

        var model = new OperationsDashboardModel();

        // Panel 1: Stale stock
        var staleEntries = await _stockLedgerService.GetStaleEntriesAsync();
        
        // Get product names
        var productNames = new Dictionary<int, string>();
        foreach (var entry in staleEntries)
        {
            if (!productNames.ContainsKey(entry.ProductId))
            {
                var product = await _stockLedgerService.GetProductByIdAsync(entry.ProductId);
                productNames[entry.ProductId] = product?.Name ?? $"Product #{entry.ProductId}";
            }
        }

        model.StaleStockRows = staleEntries.Select(e => new StaleStockRowModel
        {
            Id = e.Id,
            ProductId = e.ProductId,
            ProductName = productNames.GetValueOrDefault(e.ProductId, $"Product #{e.ProductId}"),
            WarehouseId = e.WarehouseId,
            WarehouseName = e.WarehouseId == 0 ? "Default" : $"Warehouse {e.WarehouseId}",
            StockQuantity = e.StockQuantity,
            ReservedQuantity = e.ReservedQuantity,
            LastUpdatedAtUtc = e.LastUpdatedAtUtc
        }).OrderBy(r => r.LastUpdatedAtUtc).ToList();

        // Panel 2: Outbox depth
        var unpublished = _outboxRepository.Table
            .Where(m => m.PublishedOnUtc == null)
            .ToList();

        model.TotalPendingOutbox = unpublished.Count;
        model.OutboxDepthRows = unpublished
            .GroupBy(m => m.EventType)
            .Select(g => new OutboxDepthRowModel
            {
                EventType = g.Key,
                PendingCount = g.Count(),
                OldestPendingUtc = g.Min(m => m.CreatedOnUtc)
            })
            .OrderByDescending(r => r.PendingCount)
            .ToList();

        model.OutboxAlertActive = model.OutboxDepthRows.Any(r => r.IsAlert);
        model.OldestUnpublishedCreatedAt = unpublished.Any() ? unpublished.Min(m => m.CreatedOnUtc) : null;

        // Panel 3: Circuit breaker
        model.CircuitState = _circuitMonitor.State.ToString();
        model.CircuitStateChangedAtUtc = _circuitMonitor.StateChangedAtUtc;

        return View(model);
    }
}
