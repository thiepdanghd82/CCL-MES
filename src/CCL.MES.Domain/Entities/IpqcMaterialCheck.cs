using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// IPQC first-article — row-level MATERIAL (SYSTEM) reconciliation per BOM
/// line. One row per <see cref="WoMaterial"/> of the WO (natural uniqueness
/// <c>(WorkOrderId, BomLineIdx)</c>). Mirrors the 7b <see cref="WoMaterial"/>
/// / 7d <see cref="WoIpqcCheckItem"/> shape: materialised lazily from the
/// Prepress BOM snapshot when the IPQC dashboard first opens.
///
/// Purpose (Henry 2026-08-25): at first-article, IPQC re-confirms that the
/// LOT the operator scanned in PREPRESS reconciles with the LOT that IQC
/// released at incoming inspection. The reconciliation is computed by joining
/// <c>WoMaterial → MaterialLot → IqcInspection</c>; a divergence (wrong lot,
/// IQC not Pass, part mismatch, lot not Released) is a SOFT lock — it does
/// not block judgment outright but requires an Engineer waiver.
///
/// Freeze semantics (Q2/Q4, Henry-confirmed): the divergence snapshot columns
/// are NULL until the operator's first CONFIRM (OK/NG). At confirm time the
/// controller freezes the join result into this row so the Engineer signs the
/// waiver against the exact evidence as-of the confirm — a later
/// <c>MaterialLot.Status</c> change (Released → Consumed) must NOT overwrite
/// the frozen evidence. Before first confirm the GET view computes divergence
/// live for display only.
///
/// Concurrency: intentionally NO RowVersion — consistent with
/// <see cref="WoIpqcCheck"/>. The parent WO row touch on every write bumps
/// the WO's RowVersion via the SQLite trigger, so an entity-level optimistic
/// conflict is impossible without the parent first conflicting.
/// </summary>
public class WoIpqcMaterialCheck : BaseEntity
{
    public long WorkOrderId { get; set; }

    /// <summary>Nav to the parent IPQC review row when it exists — convenience
    /// only, NOT the business key (this row materialises from
    /// <see cref="WoMaterial"/> independently of whether
    /// <see cref="WoIpqcCheck"/> has been created yet).</summary>
    public long? WoIpqcCheckId { get; set; }

    public WorkOrder? WorkOrder { get; set; }

    /// <summary>Zero-based BOM ordinal, joins back to
    /// <see cref="WoMaterial.BomLineIdx"/>. (WorkOrderId, BomLineIdx) unique.</summary>
    public int BomLineIdx { get; set; }

    // ── Snapshot at materialise (freeze — mirror WoMaterial evidence) ──
    [MaxLength(64)] public string MaterialCode { get; set; } = "";
    [MaxLength(256)] public string? MaterialDescription { get; set; }

    // ── Divergence snapshot — FROZEN at first CONFIRM (NULL before) ────
    /// <summary>IQC receipt (<c>IqcInspection.ReceiptNo</c>) resolved via the
    /// join, frozen at confirm. NULL when unresolved.</summary>
    [MaxLength(64)] public string? SourceIqcReceiptNo { get; set; }
    /// <summary><c>MaterialLot.PartNo</c> expected for this material, frozen at confirm.</summary>
    [MaxLength(64)] public string? ExpectedPartNo { get; set; }
    /// <summary>What the operator actually scanned at the machine in PREPRESS
    /// (<c>WoMaterial.LotNo</c> / <c>PartScan</c>), frozen at confirm.</summary>
    [MaxLength(64)] public string? ActualLotNo { get; set; }
    /// <summary><c>MaterialLot.Status</c> at confirm ("Released"/"Consumed"/…).</summary>
    [MaxLength(32)] public string? MaterialLotStatusSnapshot { get; set; }
    /// <summary><c>IqcInspection.Result</c> at confirm ("Pass"/"Fail"/"Pending"/null).</summary>
    [MaxLength(16)] public string? IqcResultSnapshot { get; set; }
    /// <summary>Whether the Prepress <c>MaterialLotId</c> shadow FK resolved at confirm.</summary>
    public bool HasShadowFk { get; set; }
    /// <summary>Bitmask of <see cref="DivergenceFlags"/>; 0 = fully matched.</summary>
    public int DivergenceFlags { get; set; }
    /// <summary>Human/query-friendly divergence kind (<c>DivergenceFlags.ToString()</c>);
    /// "None" when matched.</summary>
    [MaxLength(24)] public string DivergenceKind { get; set; } = "None";

    // ── Confirm OK/NG (IPQC operator) ──────────────────────────────────
    public IpqcCheckStatus Status { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? NgReasonCode { get; set; }
    [MaxLength(500)] public string? NgNote { get; set; }
    [MaxLength(128)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    // ── Engineer waiver (Q1 — soft-lock, dual-sig mirror of QA-approve) ─
    public DivergenceApprovalStatus DivergenceApprovalStatus { get; set; }
        = DivergenceApprovalStatus.NotRequired;
    /// <summary>Engineer/Supervisor username who waived the divergence. Per the
    /// dual-sig guard MUST NOT equal <see cref="ConfirmedBy"/> when flag
    /// <c>OPS_IPQC_REQUIRE_DISTINCT_MATERIAL_WAIVER</c> is ON (default).</summary>
    [MaxLength(128)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    /// <summary>Required when approval is Approved or Rejected.</summary>
    [MaxLength(500)] public string? ApprovalReason { get; set; }

    public int Sort { get; set; }
}
