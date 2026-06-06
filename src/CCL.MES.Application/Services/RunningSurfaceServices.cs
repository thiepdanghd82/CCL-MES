using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;

namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7c-1 — SETTING phase helper. Pure domain logic; the controller
/// in 7c-2 will wrap this in a single SaveChanges (mirrors the
/// 7b-2 PrepressController pattern).
///
/// Stamps <see cref="WorkOrder.SettingStartAt"/> on first entry into
/// SETTING (null-guarded so concurrent writes don't reset the clock)
/// and <see cref="WorkOrder.SettingEndAt"/> + <see cref="WorkOrder.SettingDurationSec"/>
/// on Setting Done.
/// </summary>
public static class WoSettingService
{
    /// <summary>
    /// Idempotent: stamps the start timestamp only if it's null. Multiple
    /// callers racing on PREPRESS → SETTING transition all converge on
    /// the FIRST writer's timestamp. Returns true if this call set the
    /// stamp; false if it was already set.
    /// </summary>
    public static bool MarkSettingStart(WorkOrder wo, DateTime nowUtc)
    {
        if (wo.SettingStartAt is not null) return false;
        wo.SettingStartAt = nowUtc;
        return true;
    }

    /// <summary>
    /// Stamps end + duration on SETTING → IPQC_WAIT exit. Throws
    /// <see cref="InvalidOperationException"/> if start was never
    /// stamped (caller mis-routes — controller should reject 422).
    /// </summary>
    public static int MarkSettingDone(WorkOrder wo, DateTime nowUtc)
    {
        if (wo.SettingStartAt is null)
            throw new InvalidOperationException(
                $"WO {wo.Id} cannot mark setting done — SettingStartAt is null. " +
                "Caller must reject with 422 before invoking this helper.");
        wo.SettingEndAt = nowUtc;
        var duration = (int)Math.Max(0, (nowUtc - wo.SettingStartAt.Value).TotalSeconds);
        wo.SettingDurationSec = duration;
        return duration;
    }
}

/// <summary>
/// P10.7c-1 — RUNNING session lifecycle. Each Start / Resume opens a
/// new <see cref="WoRunSession"/>; each Pause / Finish closes the
/// current one.
///
/// Does NOT call SaveChanges — the controller in 7c-2 wraps the whole
/// operation (state transition + audit emit + session write) in one
/// atomic SaveChanges per L11 (post-write ETag re-read) + L12
/// (ChangeTracker.Clear on concurrency exception).
/// </summary>
public sealed class WoRunSessionService
{
    private readonly IMesDbContext _db;

    public WoRunSessionService(IMesDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Opens a new RUNNING session. Caller is responsible for ensuring
    /// no active session exists (controller checks <see cref="GetActiveAsync"/>
    /// returns null before invoking). Mutates the WO's MesPhase to
    /// RUNNING; doesn't persist.
    /// </summary>
    public WoRunSession Start(WorkOrder wo, string actor, DateTime nowUtc)
    {
        var session = new WoRunSession
        {
            WoId = wo.Id,
            StartedAt = nowUtc,
            StartedBy = actor,
        };
        _db.WoRunSessions.Add(session);
        wo.MesPhase = MesPhase.RUNNING.ToString();
        wo.UpdatedAt = nowUtc;
        wo.UpdatedBy = actor;
        return session;
    }

    /// <summary>
    /// Closes the active session (stamps EndedAt + EndedBy). Used by
    /// Pause + Finish flows. Returns the closed session.
    /// </summary>
    public static WoRunSession Close(WoRunSession session, string actor, DateTime nowUtc)
    {
        if (session.EndedAt is not null)
            throw new InvalidOperationException(
                $"WoRunSession {session.Id} already closed at {session.EndedAt}. " +
                "Caller must reject with 422 before re-closing.");
        session.EndedAt = nowUtc;
        session.EndedBy = actor;
        session.UpdatedAt = nowUtc;
        session.UpdatedBy = actor;
        return session;
    }

    /// <summary>
    /// Finish flow: closes the active session + transitions WO to
    /// FQC_PENDING. Caller validates <see cref="WorkOrder.ProducedQty"/>
    /// (or QtyDoneCached) &gt; 0 per contract §3.2 condition.
    /// </summary>
    public void Finish(WorkOrder wo, WoRunSession activeSession, string actor, DateTime nowUtc)
    {
        Close(activeSession, actor, nowUtc);
        wo.MesPhase = MesPhase.FQC_PENDING.ToString();
        wo.UpdatedAt = nowUtc;
        wo.UpdatedBy = actor;
    }
}

/// <summary>
/// P10.7c-1 — Pause / Resume lifecycle. Pause closes the active
/// session + creates a <see cref="WoPauseEvent"/>; Resume closes the
/// pause + opens a new session.
///
/// Q6 Finish-from-PAUSED is handled by the controller: it stamps the
/// active pause's EndedAt = now BEFORE calling
/// <see cref="WoRunSessionService.Finish"/>, so total pause time stays
/// consistent (no orphan open pause survives transition to FQC_PENDING).
/// </summary>
public sealed class WoPauseService
{
    private readonly IMesDbContext _db;

    public WoPauseService(IMesDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// RUNNING → PAUSED. Closes active session, creates pause event,
    /// updates WO phase. Caller validates <paramref name="reasonCode"/>
    /// against the seeded <c>ReasonCodeKind.Pause</c> catalog (controller
    /// emits 422 prepress.invalid_reason_code shape — to be widened to
    /// run.invalid_reason_code in 7c-2). Returns the new pause event.
    /// </summary>
    public WoPauseEvent Pause(
        WorkOrder wo, WoRunSession activeSession,
        string reasonCode, string? note, string actor, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("reasonCode required", nameof(reasonCode));

        WoRunSessionService.Close(activeSession, actor, nowUtc);

        var pause = new WoPauseEvent
        {
            WoId = wo.Id,
            RunSessionId = activeSession.Id,
            StartedAt = nowUtc,
            ReasonCode = reasonCode,
            Note = note,
            StartedBy = actor,
        };
        _db.WoPauseEvents.Add(pause);
        wo.MesPhase = MesPhase.PAUSED.ToString();
        wo.UpdatedAt = nowUtc;
        wo.UpdatedBy = actor;
        return pause;
    }

    /// <summary>
    /// PAUSED → RUNNING. Closes active pause, opens a new session,
    /// updates WO phase. Returns the new session.
    /// </summary>
    public WoRunSession Resume(
        WorkOrder wo, WoPauseEvent activePause, string actor, DateTime nowUtc)
    {
        ClosePause(activePause, actor, nowUtc);

        var session = new WoRunSession
        {
            WoId = wo.Id,
            StartedAt = nowUtc,
            StartedBy = actor,
        };
        _db.WoRunSessions.Add(session);
        wo.MesPhase = MesPhase.RUNNING.ToString();
        wo.UpdatedAt = nowUtc;
        wo.UpdatedBy = actor;
        return session;
    }

    /// <summary>
    /// Q6 helper — stamps the active pause's EndedAt without
    /// transitioning to RUNNING. The controller calls this before
    /// <see cref="WoRunSessionService.Finish"/> on a Finish-from-PAUSED
    /// to ensure no orphan open pause survives the transition.
    /// </summary>
    public static void ClosePause(WoPauseEvent pause, string actor, DateTime nowUtc)
    {
        if (pause.EndedAt is not null)
            throw new InvalidOperationException(
                $"WoPauseEvent {pause.Id} already closed at {pause.EndedAt}. " +
                "Caller must reject with 422 before re-closing.");
        pause.EndedAt = nowUtc;
        pause.UpdatedAt = nowUtc;
        pause.UpdatedBy = actor;
    }
}

/// <summary>
/// P10.7c-1 — append-only qty ledger. Every operator tap is one row;
/// corrections are new rows with negative deltas + LinkedEntryId.
///
/// Updates <see cref="WorkOrder.QtyDoneCached"/> +
/// <see cref="WorkOrder.QtyNgCached"/> as a denormalised snapshot per
/// contract §5.4 — the ledger remains authoritative; the cache is
/// opportunistic.
/// </summary>
public sealed class WoQtyService
{
    private readonly IMesDbContext _db;

    public WoQtyService(IMesDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Q2 — per-tap qty add. Caller validates:
    ///   - <paramref name="qtyDoneDelta"/> &gt;= 0 (corrections use
    ///     <see cref="Correct"/>);
    ///   - if <paramref name="qtyNgDelta"/> &gt; 0 then
    ///     <paramref name="ngReasonCode"/> ∈ ReasonCodes(Kind=Scrap)
    ///     AND <paramref name="ngNote"/> non-empty.
    /// </summary>
    public WoQtyEntry Add(
        WorkOrder wo, long runSessionId,
        int qtyDoneDelta, int qtyNgDelta,
        string? ngReasonCode, string? ngNote,
        string actor, DateTime nowUtc)
    {
        if (qtyDoneDelta < 0)
            throw new ArgumentException(
                "qtyDoneDelta must be >= 0 on Add (use Correct for negative)",
                nameof(qtyDoneDelta));
        if (qtyNgDelta < 0)
            throw new ArgumentException(
                "qtyNgDelta must be >= 0 on Add (use Correct for negative)",
                nameof(qtyNgDelta));

        var entry = new WoQtyEntry
        {
            WoId = wo.Id,
            RunSessionId = runSessionId,
            Ts = nowUtc,
            QtyDoneDelta = qtyDoneDelta,
            QtyNgDelta = qtyNgDelta,
            NgReasonCode = qtyNgDelta > 0 ? ngReasonCode : null,
            NgNote = qtyNgDelta > 0 ? ngNote : null,
            EnteredBy = actor,
        };
        _db.WoQtyEntries.Add(entry);
        ApplyDeltaToCache(wo, qtyDoneDelta, qtyNgDelta, actor, nowUtc);
        return entry;
    }

    /// <summary>
    /// Q5 — append-only negative-delta correction. Caller validates:
    ///   - <paramref name="linkedEntry"/> belongs to same WO;
    ///   - <paramref name="correctionReason"/> non-empty (1-500 chars).
    /// Deltas can be negative (typically are for corrections).
    /// </summary>
    public WoQtyEntry Correct(
        WorkOrder wo, long runSessionId, WoQtyEntry linkedEntry,
        int qtyDoneDelta, int qtyNgDelta,
        string correctionReason, string actor, DateTime nowUtc)
    {
        if (linkedEntry.WoId != wo.Id)
            throw new ArgumentException(
                $"linkedEntry {linkedEntry.Id} belongs to WO {linkedEntry.WoId}, not {wo.Id}",
                nameof(linkedEntry));
        if (string.IsNullOrWhiteSpace(correctionReason))
            throw new ArgumentException("correctionReason required", nameof(correctionReason));

        var entry = new WoQtyEntry
        {
            WoId = wo.Id,
            RunSessionId = runSessionId,
            Ts = nowUtc,
            QtyDoneDelta = qtyDoneDelta,
            QtyNgDelta = qtyNgDelta,
            LinkedEntryId = linkedEntry.Id,
            CorrectionReason = correctionReason,
            EnteredBy = actor,
        };
        _db.WoQtyEntries.Add(entry);
        ApplyDeltaToCache(wo, qtyDoneDelta, qtyNgDelta, actor, nowUtc);
        return entry;
    }

    private static void ApplyDeltaToCache(WorkOrder wo, int doneDelta, int ngDelta, string actor, DateTime nowUtc)
    {
        wo.QtyDoneCached += doneDelta;
        wo.QtyNgCached += ngDelta;
        wo.UpdatedAt = nowUtc;
        wo.UpdatedBy = actor;
    }
}
