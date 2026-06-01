namespace CCL.MES.Domain.Entities;

/// <summary>
/// Phase 8 PR #28 — Spec → ProductRevision clean rewrite.
///
/// Old shape (Phase 6 baseline):  Spec → SpecVersion → SpecParameter (flat key/value).
/// New shape (SpecHub reference): ProductRevision → {SpecMaterial · SpecPrint ·
///                                                   SpecDiecut · SpecFinishing} (1:1 sibling
///                                                   tables keyed by ProductRevisionId).
///
/// Migration `AddProductRevisionSchema` preserves baseline data:
///   - SpecVersion.id=1                → ProductRevision.id=1 (preserved PK)
///   - SpecParameter rows (Width/Height/Process)
///                                     → SpecPrint.ColorSpecJson (JSON array)
///   - WorkOrder.SpecVersionId → WorkOrder.ProductRevisionId (1:1 remap by PK reuse)
///
/// Vùng cấm: IQC entity coupling KHÔNG đụng (IQC FK chỉ tới RawMaterial qua
/// RawMaterialId + PartNo snapshot — không phụ thuộc Spec).
/// </summary>
public class ProductRevision : BaseEntity
{
    public long ProductId { get; set; }
    public Product? Product { get; set; }

    /// <summary>Revision code A/B/C/AA… per SpecHub `nextRev()` letter incrementer.</summary>
    public string RevisionCode { get; set; } = "A";

    /// <summary>Lifecycle: Draft / InReview / Approved / Released / Superseded.</summary>
    public ProductRevisionStatus Status { get; set; } = ProductRevisionStatus.Draft;

    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }

    /// <summary>Lineage pointer — rev nào derive ra rev này. NULL cho rev A đầu tiên.</summary>
    public long? ParentRevisionId { get; set; }

    /// <summary>Mô tả thay đổi vs parent revision (revise reason).</summary>
    public string? ChangeSummary { get; set; }

    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }

    // ── Soft-delete cho PR #30 (Trash + Restore + 30-day purge HostedService) ─
    public bool IsTrashed { get; set; }
    public DateTime? TrashedAt { get; set; }
    public string? TrashedBy { get; set; }

    // Phase 8 PR #29 baseline title preserved cho Engineer Spec grid hiển thị.
    // SpecHub model gắn title vào artwork/customer drawing; CCL-MES giữ title
    // ở revision level cho 1:1 hiển thị ngược tương thích với Phase 7 grid.
    public string Title { get; set; } = "";

    /// <summary>Spec code (immutable across revisions of cùng 1 spec lineage).</summary>
    public string SpecCode { get; set; } = "";

    // ── Phase 8 PR #30 — list-view parity với SpecHub ────────────────────────
    // SpecHub `spec.refNo` field (e.g. `CCL-Silk-19235`) — customer-facing
    // reference distinct với SpecCode (internal lineage). Nullable cho backfill
    // sạch các baseline rev đã có; UI render "—" khi NULL.
    public string? RefNo { get; set; }

    // SpecHub `spec.inspectionLevel` field (e.g. `A166`, `A`, `B`, `C`) — quality
    // inspection grade hiển thị ở cột `Spec` của list view. Nullable.
    public string? InspectionLevel { get; set; }

    // Reverse navigations (1:1)
    public SpecMaterial? Material { get; set; }
    public SpecPrint? Print { get; set; }
    public SpecDiecut? Diecut { get; set; }
    public SpecFinishing? Finishing { get; set; }
    public List<SpecQcWindow> QcWindows { get; set; } = new();
    public List<Drawing> Drawings { get; set; } = new();
}

/// <summary>
/// 1:1 keyed by ProductRevisionId. Substrate + adhesive — common cho all label.
/// </summary>
public class SpecMaterial : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }

    public string? SubstrateType { get; set; }     // PET / PP / PVC / paper / tyvek
    public string? SubstrateBrand { get; set; }
    public int? ThicknessUm { get; set; }
    public string? LinerType { get; set; }         // pet75 / paper90 / pet50
    public string? AdhesiveType { get; set; }      // permanent / removable / freezer / high-tack
    public string? AdhesiveBrand { get; set; }
    public string? ExtraJson { get; set; }         // per-family variance
}

/// <summary>
/// 1:1 keyed by ProductRevisionId. Print process + color spec.
/// ProcessCode references ProcessCatalog.Code WHERE Category='Print'.
/// </summary>
public class SpecPrint : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }

    public string? ProcessCode { get; set; }       // FLEXO / INDIGO / SILKSCREEN / DIGITAL_UV / HYBRID
    public string? HybridProcessesJson { get; set; }
    public int NumColors { get; set; }
    public string? ColorSpecJson { get; set; }     // array of {position, type, code, anilox_lpi, process_code, …}
    public string? Varnish { get; set; }
    public string? Lamination { get; set; }
    public bool WhiteUnderprint { get; set; }
    public string? ExtraJson { get; set; }

    // ── Phase 8 PR #30 — list-view parity với SpecHub ────────────────────────
    // SpecHub `spec.printParams.printingCavity` (silkscreen) /
    // `firstPrint.plateCavity` (flexo) → unified field `Cavity`.
    public int? Cavity { get; set; }

    // SpecHub `spec.printParams.lengthPitch` (silkscreen, mm) /
    // `firstPrint.pitch` (flexo, mm). Đơn vị mm để compute cylinder gap
    // + feed-rate thực tế. Nullable cho legacy baseline.
    public double? PitchMm { get; set; }
}

/// <summary>
/// 1:1 keyed by ProductRevisionId. Die cut + perforation.
/// CutProcessCode references ProcessCatalog.Code WHERE Category='Cut'.
/// </summary>
public class SpecDiecut : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }

    public string? CutProcessCode { get; set; }    // FLATBED_CUT / ROTARY_CUT / RDC / POWERPUNCH / CNC / LASER_CUT / KISS_CUT
    public string? DieId { get; set; }
    public string? DieType { get; set; }           // magnetic / solid / laser_head / cnc_bit
    public double? WidthMm { get; set; }
    public double? LengthMm { get; set; }
    public double? CornerRadiusMm { get; set; }
    public int? KissCutDepthUm { get; set; }
    public string? PerforationJson { get; set; }
    public double? BleedMm { get; set; }
    // Process-specific params
    public string? CncProgram { get; set; }
    public double? LaserPowerW { get; set; }
    public string? PowerpunchTool { get; set; }
    public string? ExtraJson { get; set; }
}

/// <summary>
/// 1:1 keyed by ProductRevisionId. Roll/sheet output spec + post-press processes.
/// </summary>
public class SpecFinishing : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }

    public string? OutputForm { get; set; }        // roll / sheet / fanfold
    public int? LabelsPerRoll { get; set; }
    public double? CoreDiameterMm { get; set; }    // 76 / 25.4 / 152
    public string? WindingDirection { get; set; }  // in1 / in2 / in3 / in4 / out1...out4
    public double? MaxOuterDiaMm { get; set; }
    public int? RollsPerBox { get; set; }
    /// <summary>Post-press processes JSON array: [{process_code, material, sequence, …}].</summary>
    public string? FinishingProcessesJson { get; set; }
    public string? ExtraJson { get; set; }
}

/// <summary>
/// QC plan definition per revision per stage. Tạo bảng sẵn ở PR #28; UI ở PR Phase 9.
/// Approval chain reuse `spec_approval` pattern — defer Phase 9.
/// </summary>
public class SpecQcWindow : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }

    public QcStage Stage { get; set; }             // IpqcPrint / IpqcCut / Fqc / Oqc
    public string? ProcessCode { get; set; }       // nullable; restrict scope
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? SamplePlan { get; set; }        // "AQL 2.5 / Level II", "100% inline", …
    public string? Frequency { get; set; }         // start_of_run / hourly / every_box / shift_change / end_of_run
    public QcRejectAction RejectAction { get; set; } = QcRejectAction.Escalate;
    public SpecQcWindowStatus Status { get; set; } = SpecQcWindowStatus.Draft;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public List<QcCriterion> Criteria { get; set; } = new();
}

/// <summary>
/// Individual checkpoint within a QC window — what to measure + target + tolerance + method.
/// </summary>
public class QcCriterion : BaseEntity
{
    public long SpecQcWindowId { get; set; }
    public SpecQcWindow? SpecQcWindow { get; set; }

    public short Seq { get; set; }
    public string Name { get; set; } = "";
    public QcCriterionType CriterionType { get; set; } = QcCriterionType.Visual;
    public string? MeasureMethod { get; set; }
    public double? TargetValue { get; set; }
    public double? ToleranceMin { get; set; }
    public double? ToleranceMax { get; set; }
    public string? Unit { get; set; }
    public string? PassCriteria { get; set; }
    public string? ReferenceImageKey { get; set; }
    public bool Required { get; set; } = true;
    public string? ExtraJson { get; set; }
}
