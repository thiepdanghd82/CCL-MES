using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P10.7c-1 — one row per RUNNING segment of a WO. Pause + Resume
/// terminate the current row (stamps <see cref="EndedAt"/>) and open
/// a fresh one on resume. Total run time per WO = Σ
/// (EndedAt - StartedAt) across all sessions.
///
/// Concrete shape locked by contract §5.4 (P10.7-WO-STATE-CONTRACT.md).
/// </summary>
public class WoRunSession : BaseEntity
{
    public long WoId { get; set; }

    /// <summary>UTC. Stamped on POST /run/start (creates row) or on
    /// POST /run/resume (creates a new row after a pause).</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC. Null while session is live. Stamped on:
    ///   - POST /run/pause (cuts the session; resume opens a new row)
    ///   - POST /run/finish (terminates without resume; transitions
    ///     to FQC_PENDING per Q1 + Q6).</summary>
    public DateTime? EndedAt { get; set; }

    [MaxLength(128)] public string StartedBy { get; set; } = "";
    [MaxLength(128)] public string? EndedBy { get; set; }

    // Concurrency note: the session row doesn't carry its own
    // [Timestamp] RowVersion. Concurrency on Start / Pause / Resume /
    // Finish is mediated by the PARENT WorkOrder's RowVersion
    // (every transition mutates wo.MesPhase + wo.UpdatedAt) so
    // SaveChanges throws DbUpdateConcurrencyException via the WO's
    // optimistic lock if two operators race. Adding a per-session
    // [Timestamp] would require a SQLite INSERT trigger (the 7a-1.3
    // pattern) which is overkill for a child-table that never
    // changes independently of its parent.
}

/// <summary>
/// P10.7c-1 — one row per pause/resume cycle within a WO. Carries the
/// downtime reason (catalog-enforced per Q4 — must be
/// <see cref="ReasonCodeKind.Pause"/>) so OEE math can pareto-rank
/// causes.
///
/// On Q6 Finish-from-PAUSED, the controller stamps the active pause
/// row's <see cref="EndedAt"/> = now BEFORE transitioning so the WO's
/// total pause time stays consistent (no orphan open pause).
/// </summary>
public class WoPauseEvent : BaseEntity
{
    public long WoId { get; set; }

    /// <summary>FK to <see cref="WoRunSession"/>. Nullable because Q6
    /// finish-from-paused can leave a closing pause attached to the
    /// last session; future scenarios (e.g. paused-from-FQC reject)
    /// may also write a pause without an active session row. The
    /// migration column is nullable but in 7c every pause is born
    /// inside a session.</summary>
    public long? RunSessionId { get; set; }

    /// <summary>UTC. Stamped on POST /run/pause.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC. Null while paused. Stamped on POST /run/resume
    /// OR by the controller on POST /run/finish-from-PAUSED (Q6).</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Must be a <see cref="ReasonCodeKind.Pause"/> code per
    /// Q4. Controller validates against the seeded ML-* catalog
    /// (ML-MAT / ML-INK / ML-PLATE / ML-QC / ML-BREAK / ML-MTC /
    /// ML-MEET / ML-UTIL).</summary>
    [MaxLength(64)] public string ReasonCode { get; set; } = "";

    [MaxLength(500)] public string? Note { get; set; }

    [MaxLength(128)] public string StartedBy { get; set; } = "";
}

/// <summary>
/// P10.7c-1 — append-only ledger of every operator qty tap during
/// RUNNING (or PAUSED — Q5 corrections survive PAUSED so paperwork
/// catch-up is operationally smooth).
///
/// CONTRACT INVARIANT: this table is APPEND-ONLY. No UPDATE / DELETE
/// endpoints surface. A wrong entry is corrected by a NEW row with
/// negative deltas + <see cref="LinkedEntryId"/> set + non-null
/// <see cref="CorrectionReason"/> (Q5).
///
/// WO totals: <see cref="WorkOrder.QtyDoneCached"/> +
/// <see cref="WorkOrder.QtyNgCached"/> are denormalized snapshots
/// updated on every write. Authoritative source remains
/// <c>SUM(QtyDoneDelta)</c> across all rows for the WO.
/// </summary>
public class WoQtyEntry : BaseEntity
{
    public long WoId { get; set; }
    public long RunSessionId { get; set; }

    /// <summary>UTC. Server-stamped at controller entry, NOT operator-
    /// supplied. Drift-proof per S1 RCA discipline.</summary>
    public DateTime Ts { get; set; }

    /// <summary>Can be negative (Q5 correction). Cumulative WO total
    /// = SUM(QtyDoneDelta) across all rows.</summary>
    public int QtyDoneDelta { get; set; }

    /// <summary>Can be negative (Q5 correction).</summary>
    public int QtyNgDelta { get; set; }

    /// <summary>Required when <see cref="QtyNgDelta"/> &gt; 0 AND this
    /// row is NOT a correction. Must validate against
    /// <see cref="ReasonCodeKind.Scrap"/> catalog (reuses 7b-3
    /// picker source — 8 SC-* codes).</summary>
    [MaxLength(64)] public string? NgReasonCode { get; set; }

    [MaxLength(500)] public string? NgNote { get; set; }

    /// <summary>Q5 — when non-null, this row corrects a prior
    /// <see cref="WoQtyEntry"/> on the same WO. Controller validates
    /// that the linked row belongs to the same WoId.</summary>
    public long? LinkedEntryId { get; set; }

    /// <summary>Required when <see cref="LinkedEntryId"/> IS NOT
    /// NULL. 1-500 chars.</summary>
    [MaxLength(500)] public string? CorrectionReason { get; set; }

    [MaxLength(128)] public string EnteredBy { get; set; } = "";
}
