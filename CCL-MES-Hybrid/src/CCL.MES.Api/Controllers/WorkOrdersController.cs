using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Read-side surface for the legacy <see cref="WorkOrderService"/>. P10.1
/// exposes the queries that the MAUI Hybrid pilot needs to render a
/// work-order list + drawer (P10.2 default pilot pick per Q10). Write
/// surface — P10.3 W4 adds the Advance endpoint that the scan→WO flow
/// targets after the operator confirms the card. We keep the contract
/// surface minimal: only Advance lands now; Create / UpdateFlags wait
/// until the MAUI shop-floor screens land.
///
/// Default authorization = whatever the global FallbackPolicy enforces
/// (RequireAuthenticatedUser). Endpoints that need tighter gates declare
/// their own <c>[Authorize(Policy = "...")]</c>.
///
/// "Accept/Start" mapping decision (Bước 0 W4):
///   The legacy state machine has no literal "Accept" transition.
///   "Accept/Start" maps to <c>AdvanceAsync</c> — the next step per the
///   8-step flow. The button label is operator-facing convenience; the
///   server-side mutation is the existing guarded advance. This is the
///   zero-invention path and preserves the legacy guard rules.
///   If a future product call wants a distinct "claim ownership"
///   semantic we'd add an Operator + ClaimedAt column on WorkOrder —
///   that's a Domain change and belongs in a later phase.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class WorkOrdersController : ControllerBase
{
    private readonly WorkOrderService _svc;
    private readonly IAuditWriter _audit;
    public WorkOrdersController(WorkOrderService svc, IAuditWriter audit)
    {
        _svc = svc;
        _audit = audit;
    }

    /// <summary>Flat list — small datasets only. Use <c>shop-orders</c>
    /// instead for the grouped operator view.</summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkOrder>>> List() =>
        Ok(await _svc.GetAllAsync());

    [HttpGet("{id:long}")]
    public async Task<ActionResult<WorkOrder>> Get(long id)
    {
        var wo = await _svc.GetAsync(id);
        return wo is null ? NotFound() : Ok(wo);
    }

    /// <summary>
    /// Grouped shop-order view — mirrors the legacy Blazor Dashboard
    /// rollup so the MAUI pilot can render identical data.
    /// </summary>
    [HttpGet("shop-orders")]
    public async Task<ActionResult<ShopOrderListResult>> ShopOrders() =>
        Ok(await _svc.ShopOrderListAsync());

    /// <summary>
    /// Drawer payload for a single Work Order, keyed by WO number string
    /// (matches the legacy URL shape).
    /// </summary>
    [HttpGet("by-no/{woNo}")]
    public async Task<ActionResult<WorkOrderDrawerView>> Drawer(string woNo)
    {
        var view = await _svc.GetDrawerAsync(woNo);
        return view is null ? NotFound() : Ok(view);
    }

    /// <summary>
    /// P10.3 W4 — lightweight summary keyed by WO number. The scan→WO
    /// confirmation card on MAUI needs just enough to render a confidence
    /// banner ("yes we found it, here's the basic facts"); pulling the
    /// full drawer view across the wire is overkill for that moment.
    /// We reuse the existing drawer query + map to summary so the WO
    /// lookup logic stays in one place.
    /// </summary>
    [HttpGet("by-no/{woNo}/summary")]
    public async Task<ActionResult<WorkOrderSummary>> Summary(string woNo)
    {
        var view = await _svc.GetDrawerAsync(woNo);
        if (view is null)
            return NotFound(ApiError.Of("work_order.not_found", $"No work order with number '{woNo}'."));

        return Ok(new WorkOrderSummary
        {
            Id = view.Id,
            WoNo = view.WoNo,
            CustomerName = view.CustomerName,
            ProductCode = view.ProductCode,
            ProductName = view.ProductName,
            MachineCode = view.MachineCode,
            MachineName = view.MachineName,
            TargetQty = view.TargetQty,
            ProducedQty = view.ProducedQty,
            Uom = view.Uom,
            PlannedStart = view.PlannedStart is null ? null : new DateTimeOffset(DateTime.SpecifyKind(view.PlannedStart.Value, DateTimeKind.Utc)),
            PlannedEnd = view.PlannedEnd is null ? null : new DateTimeOffset(DateTime.SpecifyKind(view.PlannedEnd.Value, DateTimeKind.Utc)),
            CurrentStep = view.CurrentStep.ToString(),
            BadgeLabelKey = view.BadgeLabelKey,
            BadgeCssClass = view.BadgeCssClass,
        });
    }

    /// <summary>
    /// P10.3 W4 — Operator-driven "Accept/Start" mutation. Wraps the
    /// legacy <see cref="WorkOrderService.AdvanceAsync"/> in the API
    /// shell + audits the device id supplied via header
    /// <c>X-Device-Id</c> when present (so the audit row carries who +
    /// what station). Returns 200 with <see cref="AdvanceWorkOrderResponse"/>
    /// on both success AND domain-guard failure — the wire shape carries
    /// the error code so the client can render a Vietnamese explanation
    /// without a second round-trip. HTTP 404 only when the WO id is
    /// genuinely missing from the database.
    /// </summary>
    [HttpPost("{id:long}/advance")]
    public async Task<ActionResult<AdvanceWorkOrderResponse>> Advance(long id)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var existing = await _svc.GetAsync(id);
        if (existing is null)
            return NotFound(ApiError.Of("work_order.not_found", $"No work order with id {id}."));

        var deviceId = Request.Headers["X-Device-Id"].ToString();
        // Capture BEFORE state explicitly — the WorkOrderService.AdvanceAsync
        // re-queries the same WO in the same DbContext scope, and EF Core
        // returns the same tracked instance, so reading existing.CurrentStep
        // AFTER the call would give the AFTER value. Stringifying here pins
        // the legitimate "from" value for the audit row.
        var fromStep = existing.CurrentStep.ToString();
        var woNo = existing.WoNo;
        var result = await _svc.AdvanceAsync(id, actor);

        // Emit a paired audit row carrying the device id — the legacy service
        // already emits WO_ADVANCE without device context, so we add a thin
        // WO_ADVANCE_DEVICE marker only when the operator drove the advance
        // from a kiosk + we have a non-empty device id. This keeps the legacy
        // emit unchanged + lets device dashboards filter on TargetType=Device.
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var detail = JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = woNo,
                from = fromStep,
                to = result.CurrentStep,
                ok = result.Ok,
                error = result.ErrorCode?.ToString(),
                device_id = deviceId,
            });
            await _audit.EmitAsync(
                action: "WO_ADVANCE_DEVICE",
                actor: actor,
                actorRole: role,
                targetType: "Device",
                targetId: deviceId,
                detail: detail);
        }

        return Ok(new AdvanceWorkOrderResponse
        {
            Ok = result.Ok,
            CurrentStep = result.CurrentStep,
            ErrorCode = result.ErrorCode?.ToString(),
        });
    }
}
