using System.ComponentModel.DataAnnotations;
using CCL.MES.Domain;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P10.7e-1 Q3 — DATA-DRIVEN QC check rollup per WO per kind
/// (FQC|OQC). Mirrors the SpecHub MES_QUALITY_REDESIGN_PLAN.md profile
/// shape (12-item MES_FQC_PROFILE, 28-item MES_OQC_PROFILE) instead of
/// repeating the 7d hardcoded 4-slot mistake (Material/PrintA/PrintB/PrintC).
///
/// Henry's reminder per 7d-4 closeout (2026-06-07):
///   "Q3 schema data-driven (WoQcChecks/WoQcCheckItems) — KHÔNG
///    hardcode như 7d"
///
/// Per Q3, the item set is stored as <see cref="ProfileSnapshotJson"/>
/// (frozen at check-creation time) so profile edits don't retroactively
/// change inspections already in flight. Children are
/// <see cref="WoQcCheckItem"/> rows keyed by item key.
///
/// Per Q5, when <see cref="QcKind"/> = "OQC" the 3-signature fields
/// (Inspector + Reviewer + Approver) are filled in sequence by
/// distinct users; <see cref="Application.Services.WoQcSigPolicyOptions"/>
/// enforces the L20-default-ON distinct-user invariants server-side.
/// FQC kinds leave Reviewer + Approver null (single-sig path,
/// mirrors the 7d IPQC submitter contract).
///
/// Unique index on (WorkOrderId, QcKind) — 1 active row per kind per WO.
/// Rework cycles (FQC Reject → PREPRESS or OQC Reject → FQC_PENDING)
/// re-use the same row by resetting Judgment + child items.
/// </summary>
public class WoQcCheck : BaseEntity
{
    public long WorkOrderId { get; set; }

    /// <summary>"FQC" or "OQC" — discriminator for the row.</summary>
    [Required, MaxLength(8)]
    public string QcKind { get; set; } = "FQC";

    /// <summary>
    /// Frozen JSON snapshot of the profile (sections + items + per-item
    /// thresholds) at check-creation time. Profile edits in the
    /// admin UI (P10.7-BACKLOG OOS-C) DON'T retroactively change rows
    /// already in flight — operators see the items they agreed to
    /// check + the auditor sees the exact criterion applied.
    /// Shape: { "sections": [ { "key", "label_en", "label_vn",
    /// "items": [ { "key", "label_en", "label_vn", "threshold",
    /// "kind" } ] } ] }.
    /// </summary>
    [Required]
    public string ProfileSnapshotJson { get; set; } = "{}";

    // ── Judgment (single decision per kind) ───────────────────────
    // FQC: { Pending, Pass, Reject } → Pass advances to OQC_PENDING;
    //                                  Reject re-routes to PREPRESS.
    // OQC: { Pending, Pass, Reject } → Pass needs Approver sig +
    //                                  advances to SHIPPED;
    //                                  Reject re-loops to FQC_PENDING.

    public WoQcJudgment Judgment { get; set; } = WoQcJudgment.Pending;

    [MaxLength(500)] public string? JudgmentReason { get; set; }

    // ── Inspector signature (FQC + OQC) ───────────────────────────

    [MaxLength(64)] public string? InspectedBy { get; set; }
    public DateTime? InspectedAt { get; set; }

    // ── Reviewer signature (OQC only — null on FQC rows) ──────────
    // Q5 dual-sig: Reviewer ≠ Inspector enforced by
    // WoQcSigPolicyOptions.OqcRequireDistinctReviewer.

    [MaxLength(64)] public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }

    // ── Approver signature (OQC only — null on FQC rows) ──────────
    // Q5 triple-sig: Approver ≠ Reviewer AND (optionally) Approver ≠
    // Inspector — enforced by 2 of the 3 WoQcSigPolicyOptions flags.

    [MaxLength(64)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // ── Children navigation ───────────────────────────────────────

    public ICollection<WoQcCheckItem> Items { get; set; } = new List<WoQcCheckItem>();
}

/// <summary>
/// P10.7e-1 Q3 — single item within a <see cref="WoQcCheck"/>. Key
/// matches a profile item key from the parent's
/// <see cref="WoQcCheck.ProfileSnapshotJson"/>. Status mirrors the
/// 7d 3-state pattern (Pending/Ok/Ng) + carries the optional photo
/// blob ID per Q6 (file-picker upload in 7e-3, camera in 7f per
/// P10.7-BACKLOG OOS-B).
///
/// Unique index on (WoQcCheckId, ItemKey) — one row per item per
/// check.
/// </summary>
public class WoQcCheckItem : BaseEntity
{
    public long WoQcCheckId { get; set; }

    /// <summary>
    /// Item key — matches profile.sections[*].items[*].key. Stable
    /// across profile revisions so a future refactor can correlate
    /// the same physical check across multiple WOs.
    /// </summary>
    [Required, MaxLength(64)]
    public string ItemKey { get; set; } = "";

    public IpqcCheckStatus Status { get; set; } = IpqcCheckStatus.Pending;

    [MaxLength(64)] public string? NgReasonCode { get; set; }
    [MaxLength(500)] public string? NgNote { get; set; }

    /// <summary>
    /// Q6 — optional photo evidence. Null when no photo uploaded.
    /// Points at <see cref="WoQcPhoto.Id"/>. Operator can upload up
    /// to 4 photos per item via the file-picker flow (7e-3); only
    /// the FIRST photo's id is denormalised here for index lookup.
    /// Full list available via Photos collection on the parent.
    /// </summary>
    public long? PhotoBlobId { get; set; }

    // ── Parent navigation ─────────────────────────────────────────

    public WoQcCheck? WoQcCheck { get; set; }
}

/// <summary>
/// P10.7e-1 Q6 — photo evidence attached to a
/// <see cref="WoQcCheckItem"/>. Stored via the existing
/// <c>IBlobStore</c> co-located with the SQLite DB; filesystem path
/// resolves at request time so prod restore moves DB + blobs as one
/// unit.
/// </summary>
public class WoQcPhoto : BaseEntity
{
    public long WoQcCheckItemId { get; set; }

    /// <summary>SHA-256 of the blob bytes (lowercase hex). Lets the
    /// auditor confirm the photo on disk matches what the controller
    /// stamped (tamper detection).</summary>
    [Required, MaxLength(64)]
    public string Sha256 { get; set; } = "";

    /// <summary>MIME type at upload time — currently
    /// "image/jpeg" or "image/png" only (server enforces).</summary>
    [Required, MaxLength(32)]
    public string MimeType { get; set; } = "image/jpeg";

    /// <summary>Bytes on disk. Server caps at 5 MiB per photo.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Operator-supplied filename — kept for download UX
    /// ("save as original name"). Server sanitises before persisting
    /// the blob.</summary>
    [MaxLength(255)] public string? OriginalFileName { get; set; }

    /// <summary>Relative path under the QC photo blob root (see
    /// <c>IBlobStore.GetWoQcPhotoPath</c>); kept null on entity
    /// load + resolved at download time so a future blob-store swap
    /// (S3/Azure per OOS-A) doesn't require a row migration.</summary>
    [MaxLength(512)] public string? RelativePath { get; set; }

    [MaxLength(64)] public string? UploadedBy { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// P10.7e-1 Q3 — judgment per QC kind. Pending while the inspector
/// fills items; Pass advances the WO phase; Reject re-routes per
/// the SpecHub state-machine plan.
/// </summary>
public enum WoQcJudgment
{
    Pending = 0,
    Pass    = 1,
    Reject  = 2,
}
