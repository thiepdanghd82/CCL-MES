using System.Security.Claims;
using System.Text.Json;
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
public sealed class RunningSurfaceController : ControllerBase
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly Services.ITraceFreezeService _trace;

    public RunningSurfaceController(IMesDbContext db, IAuditWriter audit, Services.ITraceFreezeService trace)
    {
        _db = db;
        _audit = audit;
        _trace = trace;
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
        if (req.QtyDoneDelta < 0 || req.QtyNgDelta < 0)
            return Invalid("running.invalid_qty_delta",
                "Add deltas must be >= 0; use /run/qty/correct for negative.");
        if (req.QtyDoneDelta == 0 && req.QtyNgDelta == 0)
            return Invalid("running.invalid_qty_delta",
                "At least one of QtyDoneDelta or QtyNgDelta must be > 0.");

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
        if (string.IsNullOrWhiteSpace(req.CorrectionReason) || req.CorrectionReason.Length > 500)
            return Invalid("running.invalid_correction_reason",
                "CorrectionReason is required (1-500 chars).");

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

        if (req is null || string.IsNullOrWhiteSpace(req.ReasonCode))
            return Invalid("running.invalid_reason_code", "ReasonCode is required.");
        if (req.Note is not null && req.Note.Length > 500)
            return Invalid("running.invalid_note", "Note must be 0-500 chars.");

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
        if (string.IsNullOrWhiteSpace(ngReasonCode))
            return Invalid("running.invalid_reason_code",
                $"NgReasonCode is required when QtyNgDelta > 0.");
        if (string.IsNullOrWhiteSpace(ngNote) || ngNote.Length > 500)
            return Invalid("running.invalid_ng_note",
                "NgNote must be 1-500 chars when QtyNgDelta > 0.");

        var exists = await _db.ReasonCodes.AsNoTracking()
            .AnyAsync(r => r.Code == ngReasonCode && r.Kind == kind);
        if (!exists)
            return Invalid("running.invalid_reason_code",
                $"NgReasonCode '{ngReasonCode}' is not a registered {kind} reason.");
        return null;
    }

    private IActionResult Invalid(string code, string detail)
        => UnprocessableEntity(ApiError.Of(code, detail));

    // ── Prelude: If-Match + Idempotency-Key + RowVersion check ────

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
        // RowVersion + EF [Timestamp] concurrency check fires
        // correctly under parallel writes (matches 7b-2 atomic
        // pattern). Domain services already set UpdatedAt/By on the
        // entities they mutate; this is the belt-and-suspenders.
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;

        try
        {
            // SINGLE SaveChanges commits the service-mutated rows + the
            // WO touch atomically. Race losers throw here.
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return await HandleConcurrencyAsync(woId, wo, actor, role, action);
        }

        // Post-save: re-read RowVersion via AsNoTracking (Lesson L11 —
        // SQLite UPDATE trigger fires AFTER the RETURNING clause).
        var freshRowVersion = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking()
            .Select(w => w.RowVersion).SingleOrDefaultAsync();
        var newEtagRaw = freshRowVersion is not null && freshRowVersion.Length > 0
            ? Convert.ToBase64String(freshRowVersion) : "";

        Response.Headers.ETag = $"\"{newEtagRaw}\"";

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
            ETag = newEtagRaw,
            MesPhase = wo.MesPhase,
            QtyDoneCached = wo.QtyDoneCached,
            QtyNgCached = wo.QtyNgCached,
        });
    }

    private async Task<IActionResult> HandleConcurrencyAsync(
        long woId, WorkOrder wo, string actor, string role, string attemptedAction)
    {
        // L12 — clear change tracker BEFORE the next read so downstream
        // middleware SaveChanges doesn't replay the failed write.
        if (_db is Microsoft.EntityFrameworkCore.DbContext dbCtx)
            dbCtx.ChangeTracker.Clear();

        var freshRv = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking()
            .Select(w => new { w.RowVersion, w.MesPhase, w.QtyDoneCached, w.QtyNgCached })
            .SingleOrDefaultAsync();
        var freshEtag = freshRv?.RowVersion is not null && freshRv.RowVersion.Length > 0
            ? Convert.ToBase64String(freshRv.RowVersion) : "";
        Response.Headers.ETag = $"\"{freshEtag}\"";

        // L45 — nhánh này CŨNG phải để lại vết.
        //
        // Trước đây chỉ nhánh pre-check (phát hiện ETag cũ TRƯỚC khi ghi) emit
        // WO_STATE_CONFLICT. Nhánh này là cuộc đua THẬT — hai operator cùng ghi,
        // kẻ thua vỡ ở SaveChanges — và nó trả 409 mà không ghi audit dòng nào.
        // Đo được: 10 request song song ⇒ wire LUÔN đúng 9 × 409 (tất định),
        // nhưng audit chỉ 3–9 dòng tuỳ lần chạy, chờ 3s không hội tụ ⇒ mất thật,
        // không phải ghi trễ. Đúng kịch bản mà P10.7-WO-STATE-CONTRACT sinh ra để
        // bảo vệ (§B.8 "per-shift state machine corruption") lại là kịch bản
        // không truy được.
        //
        // Emit PHẢI đứng SAU ChangeTracker.Clear(): ApiAuditWriter dùng CHUNG
        // DbContext scoped của request; nếu tracker còn giữ UPDATE đã fail thì
        // SaveChanges của audit sẽ kéo theo nó và ném lại DbUpdateConcurrencyException,
        // nuốt mất dòng audit lần nữa.
        await _audit.EmitAsync(
            action: AuditAction.WoStateConflict,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = woId,
                wo_no = wo.WoNo,
                attempted_action = attemptedAction,
                client_version = NormalizeETag(Request.Headers.IfMatch.ToString()),
                server_version = freshEtag,
                source = "ef_concurrency",
            }));
        return Conflict(new RunningSurfaceSetResponse
        {
            Ok = false,
            ErrorCode = "wo.state_conflict",
            ETag = freshEtag,
            MesPhase = freshRv?.MesPhase ?? wo.MesPhase,
            QtyDoneCached = freshRv?.QtyDoneCached ?? wo.QtyDoneCached,
            QtyNgCached = freshRv?.QtyNgCached ?? wo.QtyNgCached,
        });
    }

    private static string NormalizeETag(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(2);
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s.Substring(1, s.Length - 2);
        return s;
    }
}
