using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Nop.Services.Integration.Messaging;

namespace Nop.Web.Areas.Admin.Controllers;

/// <summary>
/// VerdeMart Phase 1 sanity endpoint. Triggers a fire-and-forget publish so
/// the operator can verify the RabbitMQ pipeline end-to-end in the management
/// UI. To be removed (or hidden behind a feature flag) when Phase 2 lands and
/// the outbox becomes the canonical publish path.
/// </summary>
public partial class IntegrationDiagnosticsController : BaseAdminController
{
    protected readonly IRabbitMqPublisher _publisher;

    public IntegrationDiagnosticsController(IRabbitMqPublisher publisher)
    {
        _publisher = publisher;
    }

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
}
