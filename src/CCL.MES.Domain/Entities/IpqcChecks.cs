using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P10.7d-1 — single-row IPQC review per WO (1:1, enforced by unique
/// index on <see cref="WorkOrderId"/>). Mirrors the 7b
/// <c>WoPlateCheck</c> / <c>WoCutterCheck</c> shape but with 4 status
/// slots (Material + 3 print checks per SpecHub §3 line 130) + a
/// judgment trio + a QA approval trio.
///
/// Per Q1 (Henry-confirmed 2026-06-06), the 3 print-check slots are
/// hardcoded at the entity level — Color (ΔE default 2) / Registration
/// / Content (text+barcode). Per-product threshold override defers
/// to 7e.
///
/// Per Q2, single row not per-row table. Audit granularity per Q7 is
/// achieved at the controller layer (one <c>WO_IPQC_CHECK</c> audit
/// row per slot mutation), not by sharding the entity.
///
/// Per §5.1 overwriteable until QC submits a judgment; once
/// <see cref="Judgment"/> is non-Pending the controller rejects
/// further slot mutations with 422 invalid_phase.
/// </summary>
public class WoIpqcCheck : BaseEntity
{
    public long WorkOrderId { get; set; }

    // ── ① Material recheck (lot-level, NOT per-material) ─────────
    // SpecHub §3 line 129: "verify lại lần cuối, OK/NG cho cả lô".

    public IpqcCheckStatus MaterialStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? MaterialNgReasonCode { get; set; }
    [MaxLength(500)] public string? MaterialNgNote { get; set; }

    // ── ② Print check A — Color (default ΔE ≤ 2) ─────────────────
    // SpecHub §3 line 132. Per-product threshold defers to 7e.

    public IpqcCheckStatus PrintAStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? PrintANgReasonCode { get; set; }
    [MaxLength(500)] public string? PrintANgNote { get; set; }

    // ── ② Print check B — Registration ───────────────────────────
    // SpecHub §3 line 133: "Registration (chồng màu)".

    public IpqcCheckStatus PrintBStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? PrintBNgReasonCode { get; set; }
    [MaxLength(500)] public string? PrintBNgNote { get; set; }

    // ── ② Print check C — Content (text + barcode) ───────────────
    // SpecHub §3 line 134: "Content (text + barcode)".

    public IpqcCheckStatus PrintCStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? PrintCNgReasonCode { get; set; }
    [MaxLength(500)] public string? PrintCNgNote { get; set; }

    // ── ③ Judgment (3-button pattern per SpecHub §3 lines 138-145) ─

    public IpqcJudgment Judgment { get; set; } = IpqcJudgment.Pending;

    /// <summary>Required when <see cref="Judgment"/> = SpecialAccept.
    /// SpecHub §3 line 144: "BẮT BUỘC nhập lý do".</summary>
    [MaxLength(500)] public string? SpecialAcceptReason { get; set; }

    /// <summary>QC username at judgment write time. Pinned to the
    /// auth claim; the dual-sig guard compares this against the
    /// QA approver's username (see §5.5.1 dual-sig contract).</summary>
    [MaxLength(128)] public string? IpqcSubmittedBy { get; set; }
    public DateTime? IpqcSubmittedAt { get; set; }

    // ── QA approval (only meaningful when Judgment = SpecialAccept) ─

    public QaOutcome QaOutcome { get; set; } = QaOutcome.Pending;

    /// <summary>Required when <see cref="QaOutcome"/> = Reject.
    /// Optional when Approve (QA can leave a note but it's not
    /// gated).</summary>
    [MaxLength(500)] public string? QaReason { get; set; }

    /// <summary>QA approver username. Per §5.5.1, MUST NOT equal
    /// <see cref="IpqcSubmittedBy"/> when feature flag
    /// `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER` is ON (default).
    /// Controller emits <c>WO_QA_APPROVE_DENIED</c> + 422
    /// <c>qa.same_user_as_ipqc_submitter</c> on violation.</summary>
    [MaxLength(128)] public string? QaApprovedBy { get; set; }
    public DateTime? QaApprovedAt { get; set; }

    // ── Concurrency (parent WO mediates per §6 + 7c-2 atomic pattern;
    //    UpdatedAt + UpdatedBy inherited from BaseEntity). Per 7b/7c
    //    convention this entity intentionally has NO RowVersion — the
    //    controller's WO row touch on every write bumps the parent's
    //    RowVersion via the existing SQLite UPDATE trigger so an
    //    optimistic-lock conflict at the entity level is impossible
    //    without the parent first conflicting. ───────────────────────
}
