using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Mapping;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly IMesDbContext _db;
    private readonly Services.ITraceIndexService _traceIndex;
    private readonly Services.WoMutationExecutor _executor;
    public WorkOrdersController(WorkOrderService svc, IAuditWriter audit, IMesDbContext db,
        Services.ITraceIndexService traceIndex, Services.WoMutationExecutor executor)
    {
        _svc = svc;
        _audit = audit;
        _db = db;
        _traceIndex = traceIndex;
        _executor = executor;
    }

    // Best-effort real-time index touch — a scan/find/advance surfaces the WO
    // in Traceability + notifies subscribers. Never breaks the primary action.
    private async Task TraceTouchSafe(string woNo)
    {
        try { await _traceIndex.TouchAsync(woNo); } catch { /* best-effort */ }
    }

    // P10.7 landing — active WOs for the scan surface (SpecHub "Active
    // Work Orders" parity). Anything not yet shipped/cancelled is "active".
    private static readonly string[] LandingActivePhases =
    {
        "PREPRESS", "SETTING", "IPQC_WAIT", "QA_PENDING", "IPQC_APPROVED",
        "RUNNING", "PAUSED", "FQC_PENDING", "OQC_PENDING",
    };

    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<ActiveWorkOrderCard>>> Active(CancellationToken ct)
    {
        var rows = await _db.WorkOrders.AsNoTracking()
            .Where(w => LandingActivePhases.Contains(w.MesPhase))
            .OrderByDescending(w => w.UpdatedAt)
            .Take(50)
            .Select(w => new ActiveWorkOrderCard
            {
                WoNo = w.WoNo,
                CustomerName = w.Customer != null ? w.Customer.Name : null,
                ProductName = w.ProductName,
                MachineCode = w.MachineCode,
                MesPhase = w.MesPhase,
                TargetQty = w.TargetQty,
                QtyDone = w.QtyDoneCached,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // P10.7 — WO-scoped audit trail for the scan-surface sidebar (SpecHub
    // parity). Any-auth (the operator viewing the WO sees its history);
    // the global /audit/log stays AdminOnly.
    [HttpGet("{id:long}/audit")]
    public async Task<ActionResult<IReadOnlyList<WoAuditEntry>>> Audit(long id, CancellationToken ct)
    {
        var idStr = id.ToString();
        var rows = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.TargetType == "WorkOrder" && a.TargetId == idStr)
            .OrderByDescending(a => a.Timestamp)
            .Take(20)
            .Select(a => new WoAuditEntry
            {
                Timestamp = a.Timestamp,
                Action = a.Action,
                ActorUsername = a.ActorUsername,
                Detail = a.Detail,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>Flat list — small datasets only. Use <c>shop-orders</c>
    /// instead for the grouped operator view.
    ///
    /// Maps to <see cref="WorkOrderListItem"/> DTO — NOT the EF entity. The
    /// service <c>.Include(Customer/Product/Inspections)</c>s navigations, and
    /// returning the entity graph 500'd every request with a System.Text.Json
    /// object cycle (Lesson L51).</summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkOrderListItem>>> List() =>
        Ok((await _svc.GetAllAsync()).Select(w => w.ToListItem()).ToList());

    [HttpGet("{id:long}")]
    public async Task<ActionResult<WorkOrderListItem>> Get(long id)
    {
        var wo = await _svc.GetAsync(id);
        if (wo is null) return NotFound();

        var dto = wo.ToListItem();
        // Surface RowVersion as ETag for the optimistic-concurrency contract,
        // mirroring the /summary endpoint (body carries it too via the DTO).
        if (!string.IsNullOrEmpty(dto.ETag))
            Response.Headers.ETag = $"\"{dto.ETag}\"";
        return Ok(dto);
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

        // A scan/find surfaces the WO in Traceability in real time (upsert
        // index + notify) — even before any phase is frozen.
        await TraceTouchSafe(woNo);

        // P10.7a-1.3 — the drawer view doesn't carry RowVersion, so
        // round-trip via the entity to expose ETag for the optimistic
        // concurrency contract. AsNoTracking + projection: cheap PK
        // lookup, no change-tracker overhead, no stale-cache risk.
        // P10.7c-3 BUG-FIX — also project MesPhase so the client can
        // dispatch routing decisions on the AUTHORITATIVE canonical
        // phase instead of the legacy CurrentStep (which doesn't change
        // in lock-step after /setting/done; CurrentStep stays "OpSetting"
        // while MesPhase advances to "IPQC_WAIT", breaking dispatch).
        var meta = await _db.WorkOrders
            .Where(w => w.Id == view.Id)
            .AsNoTracking()
            .Select(w => new { w.RowVersion, w.MesPhase })
            .SingleOrDefaultAsync();
        var rowVersion = meta?.RowVersion;
        var mesPhase = meta?.MesPhase ?? "";
        var etag = rowVersion is not null && rowVersion.Length > 0
            ? Convert.ToBase64String(rowVersion)
            : "";

        // Surface the ETag at the HTTP layer so well-behaved HTTP clients
        // can capture it via Response.Headers.ETag — and also in the body
        // for clients (e.g. browser fetch with default config) that don't
        // expose ETag through the headers API.
        if (!string.IsNullOrEmpty(etag))
            Response.Headers.ETag = $"\"{etag}\"";

        // P10.10 — Part Description synced from the IFS BOM (Structure):
        // ParentDescription of the WO's product code. First non-empty wins.
        var partDescription = string.IsNullOrEmpty(view.ProductCode)
            ? null
            : await _db.ManufacturingStructures
                .Where(ms => ms.ParentPart == view.ProductCode
                             && ms.ParentDescription != null && ms.ParentDescription != "")
                .Select(ms => ms.ParentDescription)
                .FirstOrDefaultAsync();

        return Ok(new WorkOrderSummary
        {
            Id = view.Id,
            WoNo = view.WoNo,
            CustomerName = view.CustomerName,
            ProductCode = view.ProductCode,
            ProductName = view.ProductName,
            PartDescription = partDescription,
            MachineCode = view.MachineCode,
            MachineName = view.MachineName,
            TargetQty = view.TargetQty,
            ProducedQty = view.ProducedQty,
            Uom = view.Uom,
            PlannedStart = view.PlannedStart is null ? null : new DateTimeOffset(DateTime.SpecifyKind(view.PlannedStart.Value, DateTimeKind.Utc)),
            PlannedEnd = view.PlannedEnd is null ? null : new DateTimeOffset(DateTime.SpecifyKind(view.PlannedEnd.Value, DateTimeKind.Utc)),
            CurrentStep = view.CurrentStep.ToString(),
            MesPhase = mesPhase,
            BadgeLabelKey = view.BadgeLabelKey,
            BadgeCssClass = view.BadgeCssClass,
            ETag = etag,
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
        var actorIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";

        var existing = await _svc.GetAsync(id);
        if (existing is null)
            return NotFound(ApiError.Of("work_order.not_found", $"No work order with id {id}."));

        // P10.7a-1.3 — contract §6.1 + §6.2 enforcement layer for the
        // legacy /advance endpoint. Ordering matters:
        //   1. If-Match REQUIRED (428 if missing) — RowVersion handshake.
        //   2. Idempotency-Key REQUIRED (400 if missing) — replay key.
        //   3. RowVersion compare → 409 + WO_STATE_CONFLICT audit if stale.
        //   4. Execute legacy AdvanceAsync.
        //   5. Re-fetch to read the new RowVersion (SQLite trigger bumps
        //      it AFTER the UPDATE; EF Core can't read the post-trigger
        //      value via RETURNING). Stamp ETag header + body.
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            // RFC 6585 §3 — 428 Precondition Required.
            return StatusCode(StatusCodes.Status428PreconditionRequired,
                ApiError.Of("wo.if_match_required",
                    "If-Match header required for this advance. Re-fetch the WO summary to obtain a current ETag."));
        }

        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required for this advance. Generate a UUID per intent."));
        }

        // Pre-check stale ETag → 409 + WO_STATE_CONFLICT (with actor_id, no
        // source) via the shared executor (A2-B — one WO conflict path).
        var stale = await _executor.PrecheckStaleAsync(HttpContext, existing, actor, role, "advance", actorIdStr);
        if (stale.Conflict)
            return Conflict(new AdvanceWorkOrderResponse
            {
                Ok = false,
                CurrentStep = existing.CurrentStep.ToString(),
                ErrorCode = "wo.state_conflict",
                ETag = stale.ETag,
            });

        var deviceId = Request.Headers["X-Device-Id"].ToString();
        // Capture BEFORE state explicitly — the WorkOrderService.AdvanceAsync
        // re-queries the same WO in the same DbContext scope, and EF Core
        // returns the same tracked instance, so reading existing.CurrentStep
        // AFTER the call would give the AFTER value. Stringifying here pins
        // the legitimate "from" value for the audit row.
        var fromStep = existing.CurrentStep.ToString();
        var woNo = existing.WoNo;

        AdvanceResult result;
        try
        {
            result = await _svc.AdvanceAsync(id, actor);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race lost at the SERVICE's SaveChanges (soak-test path: N parallel
            // POSTs pass the pre-check on the same RowVersion, one wins). Resolve
            // via the shared executor — L45: clear tracker → reread → emit
            // WO_STATE_CONFLICT (source=ef_concurrency + actor_id). Same 409 shape
            // as the pre-check so the client UX is identical.
            var raced = await _executor.ResolveWoConflictAsync(
                HttpContext, id, existing.WoNo, actor, role, "advance", actorIdStr);
            return Conflict(new AdvanceWorkOrderResponse
            {
                Ok = false,
                CurrentStep = existing.CurrentStep.ToString(),
                ErrorCode = "wo.state_conflict",
                ETag = raced.ETag,
            });
        }

        // Read the new RowVersion. The SQLite trigger fires AFTER the
        // UPDATE statement; EF Core's tracked entity still carries the
        // pre-trigger value because EF reads the column back in the
        // SaveChanges UPDATE RETURNING, which executes BEFORE the
        // trigger. Use AsNoTracking() to bypass the change tracker
        // entirely + read the current DB value.
        var freshRowVersion = await _db.WorkOrders
            .Where(w => w.Id == id)
            .AsNoTracking()
            .Select(w => w.RowVersion)
            .SingleOrDefaultAsync();
        var newEtagRaw = freshRowVersion is not null && freshRowVersion.Length > 0
            ? Convert.ToBase64String(freshRowVersion)
            : stale.ETag;

        Response.Headers.ETag = $"\"{newEtagRaw}\"";

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

        // Phase changed → refresh the Traceability index (CurrentMesPhase) + notify.
        if (result.Ok) await TraceTouchSafe(woNo);

        return Ok(new AdvanceWorkOrderResponse
        {
            Ok = result.Ok,
            CurrentStep = result.CurrentStep,
            ErrorCode = result.ErrorCode?.ToString(),
            ETag = newEtagRaw,
        });
    }

    // NormalizeETag removed (A2-B) — ETag pre-check + race-conflict now go through
    // WoMutationExecutor (PrecheckStaleAsync / ResolveWoConflictAsync), which owns
    // the RFC 7232 W/ + quote stripping.
}
