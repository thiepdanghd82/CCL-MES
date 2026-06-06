namespace CCL.MES.Domain;

/// <summary>7 bước của process flow đúng theo màn hình Work Order (mockup).</summary>
public enum ProcessStepCode
{
    PrePressCheck = 1,
    OpSetting     = 2,
    IpqcApproval  = 3,
    ReadyToRun    = 4,
    Running       = 5,
    Fqc           = 6,
    Oqc           = 7,
    Closed        = 8
}

public enum WoStatus { Draft, InProgress, OnHold, Finished, Closed, Cancelled }
public enum QcType { IPQC, FQC, OQC }
public enum QcResult { Pending, Pass, Fail }

/// <summary>Loại sự kiện sản xuất ghi nhận theo máy (cho OEE).</summary>
public enum ProductionEventType { Run, Stop, Setup, Idle }
public enum WiStatus { Draft, Approved, Obsolete }

// ── Phase 8 PR #28 — ProductRevision + Drawing + QC plan + ProcessCatalog ──────
// SpecStatus enum đã được REMOVED ở PR #28; replace bằng ProductRevisionStatus
// (5 state per SpecHub: Draft/InReview/Approved/Released/Superseded).

/// <summary>Spec lifecycle states per SpecHub revision model.</summary>
public enum ProductRevisionStatus { Draft, InReview, Approved, Released, Superseded }

/// <summary>Logical drawing slot per SpecHub `artwork.file_kind`.</summary>
public enum DrawingKind
{
    CustomerDrawing,        // bản vẽ gốc từ khách hàng
    NpiPrintLayout,         // bản vẽ NPI thiết kế layout in
    NpiCutLayout,           // bản vẽ NPI thiết kế layout cắt
    IpqcPrintReference,     // ảnh/sketch reference cho IPQC công đoạn in
    IpqcCutReference,       // ảnh/sketch reference cho IPQC công đoạn cắt
    FqcChecksheet,          // checksheet FQC (PDF form)
    OqcChecksheet,          // checksheet OQC
    CustomerApproval,       // proof có chữ ký approval của khách hàng
    InternalProof           // proof in nội bộ
}

public enum DrawingStatus { Draft, PendingApproval, Approved, Superseded, Withdrawn }
public enum DrawingVersionStatus { Draft, PendingApproval, Approved, Rejected, Superseded }
public enum DrawingApprovalRole { Npi, Production, Qc }
public enum DrawingApprovalStatus { Pending, Approved, Rejected }

public enum QcStage { IpqcPrint, IpqcCut, Fqc, Oqc }
public enum QcRejectAction { Rework, Scrap, Escalate, RecordOnly }
public enum SpecQcWindowStatus { Draft, Approved, Superseded }
public enum QcCriterionType { Visual, Dimensional, Colorimetric, Functional, Count }

public enum ProcessCategory { Print, Cut, Finishing }
public enum ProcessCatalogStatus { Active, Deprecated }

// ── Phase 8 PR-D-4 — QC Capture (NPI spec-level inspection result + lookup) ──

/// <summary>Operator-recorded result per QcCriterion capture event.</summary>
public enum QcCaptureResult { Pass, Fail, Na }

/// <summary>Bucket for ReasonCode lookup — Pause = downtime cause, Scrap = NG cause,
/// Recovery = admin SYS_RECOVERY justification (P10.7a-2 force-phase endpoint).</summary>
public enum ReasonCodeKind { Pause, Scrap, Recovery }

/// <summary>P10.7b-1 — status of a single PREPRESS row check (wo_materials /
/// wo_plate_check / wo_cutter_check). PENDING = operator has not touched the
/// row; OK = operator confirmed; NG = operator flagged a defect + a
/// <see cref="ReasonCodeKind.Scrap"/> code is required on the row.</summary>
public enum PrepressCheckStatus { Pending, Ok, Ng }

/// <summary>P10.7d-1 — status of a single IPQC check slot
/// (Material / PrintA / PrintB / PrintC). Identical shape to
/// <see cref="PrepressCheckStatus"/> but kept as a distinct type
/// so an IPQC entity field can't accidentally be compared against a
/// PREPRESS enum value (compile-time guard for the L19-style
/// cross-surface drift). PENDING = QC hasn't touched the slot;
/// OK = QC confirmed pass; NG = QC flagged a defect + a
/// <see cref="ReasonCodeKind.Scrap"/> code is required.</summary>
public enum IpqcCheckStatus { Pending, Ok, Ng }

/// <summary>P10.7d-1 — IPQC judgment outcome per SpecHub §3 (3-button
/// pattern). Pending = QC hasn't submitted the judgment yet (rollup
/// helper gates on this). GoRun → MesPhase IPQC_APPROVED. StopLine →
/// reset to PREPRESS. SpecialAccept → MesPhase QA_PENDING; requires
/// a free-text reason that the QA approver reviews.</summary>
public enum IpqcJudgment { Pending, GoRun, StopLine, SpecialAccept }

/// <summary>P10.7d-1 — QA approver outcome on a SPECIAL_ACCEPT
/// escalation. Pending = QA hasn't acted yet. Approve → MesPhase
/// IPQC_APPROVED + dual-sig guard enforced (QA approver ≠ IPQC
/// submitter unless `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=false`).
/// Reject → reset to PREPRESS, mirrors IPQC StopLine.</summary>
public enum QaOutcome { Pending, Approve, Reject }
