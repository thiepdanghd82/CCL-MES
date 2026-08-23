using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Policies;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.RunningSurface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.7c-2 — SETTING + RUNNING + PAUSED write surface per contract
/// §5.2 amendment.
///
/// Endpoints:
///   POST /api/v2/work-orders/{id}/setting/done       SETTING → IPQC_WAIT (Q1)
///   POST /api/v2/work-orders/{id}/run/start          IPQC_APPROVED → RUNNING (Q3 OP+Production)
///   POST /api/v2/work-orders/{id}/run/qty            Per-tap qty add (Q2)
///   POST /api/v2/work-orders/{id}/run/qty/correct    Append-only correction (Q5)
///   POST /api/v2/work-orders/{id}/run/pause          RUNNING → PAUSED (Q4 catalog-only)
///   POST /api/v2/work-orders/{id}/run/resume         PAUSED → RUNNING
///   POST /api/v2/work-orders/{id}/run/finish         RUNNING|PAUSED → FQC_PENDING (Q6)
///
/// Concurrency contract mirrors /advance (7a-1.3) + /prepress (7b-2):
///   428 missing If-Match
///   400 missing Idempotency-Key
///   404 WO not found
///   409 wo.state_conflict on stale If-Match (+ WO_STATE_CONFLICT audit)
///   422 wo.invalid_phase / running.invalid_status / running.invalid_reason_code
///       / running.invalid_ng_note / running.invalid_qty_delta / running.invalid_correction_reason
///
/// ATOMIC PATTERN — every endpoint:
///   1. Prelude (If-Match + Idem-Key + WO fetch + RowVersion check)
///   2. Body + catalog validation
///   3. Phase guard
///   4. Domain service call (mutates entities; does NOT SaveChanges)
///   5. wo.UpdatedAt = now + wo.UpdatedBy = actor (forces RowVersion bump)
///   6. SINGLE SaveChanges (catch DbUpdateConcurrencyException → 409)
///   7. Audit emit (after successful SaveChanges)
///   8. Return 200 + post-write state (MesPhase + QtyDoneCached + new ETag)
///
/// The single-SaveChanges + WO row touch pattern is what closes the
/// rollup-race condition Henry flagged as critical condition #1 in
/// the 7b-2 breakdown. SQLite write-lock + EF [Timestamp] serialise
/// concurrent operators — race losers throw DbUpdateConcurrencyException
/// in step 6 and land in HandleConcurrency as 409.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class RunningSurfaceController : WoMutationControllerBase
{
    private readonly Services.ITraceFreezeService _trace;
    private readonly Services.WoMutationExecutor _executor;

    public RunningSurfaceController(IMesDbContext db, IAuditWriter audit,
        Services.ITraceFreezeService trace, Services.WoMutationExecutor executor)
        : base(db, audit)
    {
        _trace = trace;
        _executor = executor;
    }

    // ── GET /running-surface ───────────────────────────────────────

    /// <summary>P10.7c-3 — read view for the SETTING + RUNNING + PAUSED
    /// dashboards. Single round-trip returns every field the operator UI
    /// needs to render the next state (active session/pause + last 20
    /// qty entries for the correction picker). ETag mirrors the WO's
    /// current RowVersion so the caller can use it as <c>If-Match</c> on
    /// the next mutation without a second hop.</summary>
    [HttpGet("{id:long}/running-surface")]
    public async Task<IActionResult> GetRunningSurface(long id, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        var etag = Convert.ToBase64String(wo.RowVersion);

        var activeSession = await _db.WoRunSessions.AsNoTracking()
            .Where(s => s.WoId == id && s.EndedAt == null)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        var activePause = await _db.WoPauseEvents.AsNoTracking()
            .Where(p => p.WoId == id && p.EndedAt == null)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync(ct);

        var recentEntries = await _db.WoQtyEntries.AsNoTracking()
            .Where(e => e.WoId == id)
            .OrderByDescending(e => e.Id)
            .Take(20)
            .Select(e => new RunningQtyEntryRow
            {
                EntryId = e.Id,
                CreatedAt = e.Ts,
                QtyDoneDelta = e.QtyDoneDelta,
                QtyNgDelta = e.QtyNgDelta,
                NgReasonCode = e.NgReasonCode,
                NgNote = e.NgNote,
                EnteredBy = e.EnteredBy,
                LinkedEntryId = e.LinkedEntryId,
                CorrectionReason = e.CorrectionReason,
            })
            .ToListAsync(ct);

        var view = new RunningSurfaceView
        {
            WoId = wo.Id,
            WoNo = wo.WoNo,
            MesPhase = wo.MesPhase,
            ETag = etag,
            TargetQty = wo.TargetQty,
            QtyDoneCached = wo.QtyDoneCached,
            QtyNgCached = wo.QtyNgCached,
            SettingStartAt = wo.SettingStartAt,
            SettingEndAt = wo.SettingEndAt,
            SettingDurationSec = wo.SettingDurationSec,
            ActiveSessionId = activeSession?.Id,
            ActiveSessionStartAt = activeSession?.StartedAt,
            ActivePauseId = activePause?.Id,
            ActivePauseStartAt = activePause?.StartedAt,
            ActivePauseReasonCode = activePause?.ReasonCode,
            ActivePauseNote = activePause?.Note,
            RecentEntries = recentEntries,
        };

        Response.Headers.ETag = $"\"{etag}\"";
        return Ok(view);
    }

    // ── POST /setting/enter ────────────────────────────────────────

    /// <summary>P10.7c-3 — idempotent stamp of SettingStartAt. Closes
    /// the gap that /advance landed the WO in SETTING without starting
    /// the timer. The dashboard fires this once on first load when
    /// MesPhase = SETTING + SettingStartAt is null; idempotent if the
    /// stamp already exists (returns 200 + current ETag without bumping
    /// RowVersion if no mutation actually happened — relies on the WO
    /// touch in CommitAndAudit to bump anyway on the FIRST call only,
    /// which is correct because the second-and-later callers see
    /// SettingStartAt non-null + bail out of the helper.)</summary>
    [HttpPost("{id:long}/setting/enter")]
    public async Task<IActionResult> PostSettingEnter(
        long id, [FromBody] SettingEnterRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "setting_enter");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "SETTING")
            return Invalid("wo.invalid_phase",
                $"setting/enter requires MesPhase = SETTING; current = {wo.MesPhase}.");

        var stamped = WoSettingService.MarkSettingStart(wo, DateTime.UtcNow);

        var result = await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoSettingStart,
            new { setting_start_stamped = stamped });
        // WO has left PREPRESS → freeze the Product-data snapshot (material +
        // plate + cutter are complete + immutable now). Best-effort.
        if (result is OkObjectResult)
            await FreezeSafe(id, CCL.MES.Shared.Quality.TracePhase.Product, actor);
        return result;
    }

    // Best-effort trace freeze — a snapshot failure must NEVER break the
    // confirm that triggered it. Idempotent by ContentHash in the service.
    private async Task FreezeSafe(long woId, string phase, string actor)
    {
        try { await _trace.FreezeAsync(woId, phase, actor); }
        catch { /* best-effort side-effect; confirm already committed */ }
    }

    // ── POST /setting/done ─────────────────────────────────────────

    [HttpPost("{id:long}/setting/done")]
    public async Task<IActionResult> PostSettingDone(
        long id, [FromBody] SettingDoneRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "setting_done");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "SETTING")
            return Invalid("wo.invalid_phase",
                $"setting/done requires MesPhase = SETTING; current = {wo.MesPhase}.");

        if (wo.SettingStartAt is null)
            return Invalid("running.setting_not_started",
                "SettingStartAt is null — caller never entered SETTING. Use admin force-phase.");

        var now = DateTime.UtcNow;
        var duration = WoSettingService.MarkSettingDone(wo, now);
        wo.MesPhase = "IPQC_WAIT";

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoSettingDone,
            new { duration_sec = duration });
    }

    // ── POST /run/start ────────────────────────────────────────────

    [HttpPost("{id:long}/run/start")]
    public async Task<IActionResult> PostRunStart(
        long id, [FromBody] RunStartRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "run_start");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "IPQC_APPROVED")
            return Invalid("wo.invalid_phase",
                $"run/start requires MesPhase = IPQC_APPROVED; current = {wo.MesPhase}.");

        var svc = new WoRunSessionService(_db);
        var session = svc.Start(wo, actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoRunStart,
            new { run_session_pending = true });
    }

    // ── POST /run/qty ──────────────────────────────────────────────

    [HttpPost("{id:long}/run/qty")]
    public async Task<IActionResult> PostRunQty(
        long id, [FromBody] RunQtyAddRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "run_qty_add");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "RUNNING")
            return Invalid("wo.invalid_phase",
                $"run/qty requires MesPhase = RUNNING; current = {wo.MesPhase}.");

        if (req is null)
            return Invalid("running.invalid_body", "Body is required.");
        var bodyErr = RunningSurfacePolicy.ValidateQtyAdd(req);
        if (bodyErr is not null) return Invalid(bodyErr.Value.ErrorCode, bodyErr.Value.Message);

        if (req.QtyNgDelta > 0)
        {
            var ngErr = await ValidateNgAsync(req.NgReasonCode, req.NgNote, ReasonCodeKind.Scrap);
            if (ngErr is not null) return ngErr;
        }

        var activeSession = await GetActiveSessionAsync(id);
        if (activeSession is null)
            return Invalid("running.no_active_session",
                "No active WoRunSession for this WO — start /run/start first.");

        var svc = new WoQtyService(_db);
        var entry = svc.Add(wo, activeSession.Id,
            req.QtyDoneDelta, req.QtyNgDelta,
            req.NgReasonCode, req.NgNote, actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoRunQtyAdd,
            new
            {
                run_session_id = activeSession.Id,
                qty_done_delta = req.QtyDoneDelta,
                qty_ng_delta = req.QtyNgDelta,
                ng_reason_code = req.NgReasonCode,
                ng_note = req.NgNote,
            });
    }

    // ── POST /run/qty/correct ──────────────────────────────────────

    [HttpPost("{id:long}/run/qty/correct")]
    public async Task<IActionResult> PostRunQtyCorrect(
        long id, [FromBody] RunQtyCorrectRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "run_qty_correct");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        // Q5: correction allowed in RUNNING OR PAUSED (paperwork catch-up
        // is operationally common during pauses).
        if (wo.MesPhase != "RUNNING" && wo.MesPhase != "PAUSED")
            return Invalid("wo.invalid_phase",
                $"run/qty/correct requires MesPhase ∈ {{RUNNING, PAUSED}}; current = {wo.MesPhase}.");

        if (req is null)
            return Invalid("running.invalid_body", "Body is required.");
        var bodyErr = RunningSurfacePolicy.ValidateQtyCorrect(req);
        if (bodyErr is not null) return Invalid(bodyErr.Value.ErrorCode, bodyErr.Value.Message);

        var linkedEntry = await _db.WoQtyEntries.FirstOrDefaultAsync(e => e.Id == req.LinkedEntryId);
        if (linkedEntry is null)
            return Invalid("running.linked_entry_not_found",
                $"LinkedEntryId {req.LinkedEntryId} not found.");
        if (linkedEntry.WoId != id)
            return Invalid("running.linked_entry_wrong_wo",
                $"LinkedEntryId {req.LinkedEntryId} belongs to WO {linkedEntry.WoId}, not {id}.");

        // Determine session id — use the linked entry's session (corrections
        // attach to the same session for traceability).
        var sessionId = linkedEntry.RunSessionId;

        var svc = new WoQtyService(_db);
        var entry = svc.Correct(wo, sessionId, linkedEntry,
            req.QtyDoneDelta, req.QtyNgDelta,
            req.CorrectionReason, actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoRunQtyCorrect,
            new
            {
                run_session_id = sessionId,
                qty_done_delta = req.QtyDoneDelta,
                qty_ng_delta = req.QtyNgDelta,
                linked_entry_id = req.LinkedEntryId,
                correction_reason = req.CorrectionReason,
            });
    }

    // ── POST /run/pause ────────────────────────────────────────────

    [HttpPost("{id:long}/run/pause")]
    public async Task<IActionResult> PostRunPause(
        long id, [FromBody] RunPauseRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "run_pause");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "RUNNING")
            return Invalid("wo.invalid_phase",
                $"run/pause requires MesPhase = RUNNING; current = {wo.MesPhase}.");

        if (req is null)
            return Invalid("running.invalid_reason_code", "ReasonCode is required.");
        var fmtErr = RunningSurfacePolicy.ValidatePauseFormat(req);
        if (fmtErr is not null) return Invalid(fmtErr.Value.ErrorCode, fmtErr.Value.Message);

        var pauseReasonOk = await _db.ReasonCodes.AsNoTracking()
            .AnyAsync(r => r.Code == req.ReasonCode && r.Kind == ReasonCodeKind.Pause);
        if (!pauseReasonOk)
            return Invalid("running.invalid_reason_code",
                $"ReasonCode '{req.ReasonCode}' is not a registered Pause reason.");

        var activeSession = await GetActiveSessionAsync(id);
        if (activeSession is null)
            return Invalid("running.no_active_session",
                "No active WoRunSession to pause.");

        var svc = new WoPauseService(_db);
        svc.Pause(wo, activeSession, req.ReasonCode, req.Note, actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoRunPause,
            new
            {
                run_session_id = activeSession.Id,
                reason_code = req.ReasonCode,
                note = req.Note,
            });
    }

    // ── POST /run/resume ───────────────────────────────────────────

    [HttpPost("{id:long}/run/resume")]
    public async Task<IActionResult> PostRunResume(
        long id, [FromBody] RunResumeRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "run_resume");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "PAUSED")
            return Invalid("wo.invalid_phase",
                $"run/resume requires MesPhase = PAUSED; current = {wo.MesPhase}.");

        var activePause = await GetActivePauseAsync(id);
        if (activePause is null)
            return Invalid("running.no_active_pause",
                "No active WoPauseEvent to resume.");

        var svc = new WoPauseService(_db);
        svc.Resume(wo, activePause, actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoRunResume,
            new { pause_event_id = activePause.Id });
    }

    // ── POST /run/finish ───────────────────────────────────────────

    [HttpPost("{id:long}/run/finish")]
    public async Task<IActionResult> PostRunFinish(
        long id, [FromBody] RunFinishRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "run_finish");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        // Q6: Finish accepted from RUNNING OR PAUSED.
        if (wo.MesPhase != "RUNNING" && wo.MesPhase != "PAUSED")
            return Invalid("wo.invalid_phase",
                $"run/finish requires MesPhase ∈ {{RUNNING, PAUSED}}; current = {wo.MesPhase}.");

        // §3.2 condition: ProducedQty > 0 OR QtyDoneCached > 0 (the cache
        // is the source of truth in 7c since /run/qty maintains it).
        if (wo.QtyDoneCached <= 0)
            return Invalid("running.no_production",
                "Cannot finish — QtyDoneCached is 0 (no qty entered).");

        var now = DateTime.UtcNow;
        var fromPhase = wo.MesPhase;

        // Q6 helper: if PAUSED, stamp the active pause's EndedAt = now so
        // total pause time stays consistent. Then close the active session
        // (which is already closed if PAUSED, but Finish from RUNNING
        // still needs to close it).
        WoPauseEvent? closedPause = null;
        WoRunSession activeSession;

        if (fromPhase == "PAUSED")
        {
            var activePause = await GetActivePauseAsync(id);
            if (activePause is null)
                return Invalid("running.no_active_pause",
                    "PAUSED phase but no active WoPauseEvent — caller must reset state.");
            WoPauseService.ClosePause(activePause, actor, now);
            closedPause = activePause;
            // Active session: the most recently STARTED session for the WO
            // (even though it's already closed when we entered PAUSED).
            // For Finish-from-PAUSED, we don't open a new session; we
            // just close the pause + transition phase.
            activeSession = await _db.WoRunSessions
                .Where(s => s.WoId == id)
                .OrderByDescending(s => s.Id)
                .FirstAsync();
            wo.MesPhase = "FQC_PENDING";
            wo.UpdatedAt = now;
            wo.UpdatedBy = actor;
        }
        else // RUNNING
        {
            var maybeSession = await GetActiveSessionAsync(id);
            if (maybeSession is null)
                return Invalid("running.no_active_session",
                    "RUNNING phase but no active WoRunSession.");
            activeSession = maybeSession;
            var svc = new WoRunSessionService(_db);
            svc.Finish(wo, activeSession, actor, now);
        }

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoRunFinish,
            new
            {
                from_phase = fromPhase,
                run_session_id = activeSession.Id,
                pause_event_id = closedPause?.Id,
                qty_done_cached = wo.QtyDoneCached,
                qty_ng_cached = wo.QtyNgCached,
            });
    }

    // ── Helpers ───────────────────────────────────────────────────

    private async Task<WoRunSession?> GetActiveSessionAsync(long woId)
        => await _db.WoRunSessions
            .Where(s => s.WoId == woId && s.EndedAt == null)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();

    private async Task<WoPauseEvent?> GetActivePauseAsync(long woId)
        => await _db.WoPauseEvents
            .Where(p => p.WoId == woId && p.EndedAt == null)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();

    private async Task<IActionResult?> ValidateNgAsync(
        string? ngReasonCode, string? ngNote, ReasonCodeKind kind)
    {
        var fmtErr = RunningSurfacePolicy.ValidateNgFormat(ngReasonCode, ngNote);
        if (fmtErr is not null) return Invalid(fmtErr.Value.ErrorCode, fmtErr.Value.Message);

        var exists = await _db.ReasonCodes.AsNoTracking()
            .AnyAsync(r => r.Code == ngReasonCode && r.Kind == kind);
        if (!exists)
            return Invalid("running.invalid_reason_code",
                $"NgReasonCode '{ngReasonCode}' is not a registered {kind} reason.");
        return null;
    }

    // ── Prelude: If-Match + Idempotency-Key + RowVersion check ────

    // A2 thin-controller — GIỮ INLINE (không gộp vào base). The base
    // WoMutationControllerBase.PreludeAsync onConflict factory only exposes
    // (serverEtag, mesPhase); the RunningSurface 409 body carries TWO extra
    // fields read off the stale `wo` (QtyDoneCached + QtyNgCached) that the
    // factory can't reach without a per-request stash — that would either drift
    // the byte-identical contract or introduce a race-unsafe field. The
    // mechanism (Idem-Key/If-Match/404/ETag-compare + WoStateConflict audit +
    // ETag header) is identical to the base; only the typed conflict body's
    // extra qty fields keep it inline. Flagged in the A2 report.
    private async Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
    {
        var idemKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idemKey))
        {
            return (BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required.")), null);
        }

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return (StatusCode(StatusCodes.Status428PreconditionRequired,
                ApiError.Of("wo.if_match_required",
                    "If-Match header required.")), null);
        }

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null)
        {
            return (NotFound(ApiError.Of("wo.not_found",
                $"No work order with id {id}.")), null);
        }

        var serverEtagRaw = Convert.ToBase64String(wo.RowVersion);
        var clientEtagRaw = NormalizeETag(ifMatch);
        if (!string.Equals(serverEtagRaw, clientEtagRaw, StringComparison.Ordinal))
        {
            var conflictDetail = JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = wo.WoNo,
                attempted_action = attemptedAction,
                client_version = clientEtagRaw,
                server_version = serverEtagRaw,
            });
            await _audit.EmitAsync(
                action: AuditAction.WoStateConflict,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: id.ToString(),
                detail: conflictDetail);

            Response.Headers.ETag = $"\"{serverEtagRaw}\"";
            return (Conflict(new RunningSurfaceSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = serverEtagRaw,
                MesPhase = wo.MesPhase,
                QtyDoneCached = wo.QtyDoneCached,
                QtyNgCached = wo.QtyNgCached,
            }), null);
        }

        return (null, wo);
    }

    // ── Commit: SINGLE SaveChanges + post-write ETag re-read + audit ─

    private async Task<IActionResult> CommitAndAuditAsync(
        long woId, WorkOrder wo,
        string actor, string role,
        string action, object extraDetail)
    {
        // Always touch the WO row so the SQLite UPDATE trigger bumps
        // RowVersion + EF [Timestamp] concurrency check fires correctly under
        // parallel writes. Then hand the atomic save + L45 conflict path to the
        // shared executor (A2 — SaveChanges lives in Services/, not the controller).
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;

        var outcome = await _executor.SaveAndResolveAsync(HttpContext, woId, wo.WoNo, actor, role, action);
        if (outcome.Conflict)
            return Conflict(new RunningSurfaceSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = outcome.ETag,
                MesPhase = outcome.Fresh?.MesPhase ?? wo.MesPhase,
                QtyDoneCached = outcome.Fresh?.QtyDoneCached ?? wo.QtyDoneCached,
                QtyNgCached = outcome.Fresh?.QtyNgCached ?? wo.QtyNgCached,
            });

        var detailObj = new
        {
            wo_id = woId,
            wo_no = wo.WoNo,
            mes_phase_after = wo.MesPhase,
            qty_done_cached_after = wo.QtyDoneCached,
            qty_ng_cached_after = wo.QtyNgCached,
            extra = extraDetail,
        };
        await _audit.EmitAsync(
            action: action,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(detailObj));

        return Ok(new RunningSurfaceSetResponse
        {
            Ok = true,
            ETag = outcome.ETag,
            MesPhase = wo.MesPhase,
            QtyDoneCached = wo.QtyDoneCached,
            QtyNgCached = wo.QtyNgCached,
        });
    }
}
