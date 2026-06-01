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

    // ── Phase 8 PR #31a — silkscreen print rows child entity ────────────────
    // SpecHub `spec.printRows[]` — per-color print spec (20 field). Trước đây
    // fold vào ColorSpecJson; sau PR #31a tách bảng để query/filter/index được
    // (PlateCode/InkCode/Color search trong PR #33 detail sheet).
    // ColorSpecJson vẫn giữ cho fallback legacy + extra fields chưa migrate.
    public List<SpecPrintColor> Colors { get; set; } = new();

    // ── Phase 8 PR #31b — Flexo child rows ──────────────────────────────────
    // SpecHub flexo template chứa 3 data tables riêng:
    //   - PrintingRows (cylinder/material/tension) → fold vào ExtraJson per Q3
    //     PR #31b (3-4 rows mỗi spec, ít cần query column-wise)
    //   - CuttingRows (die cut + lamination + cutter) → SpecFlexoCuttingRow
    //   - InkRows (per-color ink + anilox + UV/IR power) → SpecFlexoInkRow
    // Sub-tab nav PR #33 sẽ render từ 2 entity này; current PR chỉ persist.
    public List<SpecFlexoCuttingRow> FlexoCuttingRows { get; set; } = new();
    public List<SpecFlexoInkRow> FlexoInkRows { get; set; } = new();
}

/// <summary>
/// Phase 8 PR #31a — Silkscreen print rows child entity. 1:N keyed by
/// SpecPrintId. Mirror SpecHub `printRows[]` shape (20 field per color).
/// Future PR may extend cho indigo + flexo subtypes (reuse fields where
/// overlap; FlexoPrint/FlexoCut/FlexoInk có entity riêng PR #31b).
/// </summary>
public class SpecPrintColor : BaseEntity
{
    public long SpecPrintId { get; set; }
    public SpecPrint? SpecPrint { get; set; }

    /// <summary>1-based print sequence (No. column in xlsx).</summary>
    public int Seq { get; set; }

    public string? Surface { get; set; }            // R / S / R+S
    public string? Color { get; set; }              // "WN-212", "PANTONE 186 C"
    public string? InkName { get; set; }            // "CCLISOL-1160"
    public string? InkCode { get; set; }            // "HI1160"
    public string? Maker { get; set; }              // "CCL MIX", "SEIKO"
    public string? Retarder { get; set; }           // additive code
    public double? Viscosity { get; set; }
    public double? Speed { get; set; }              // shoot/min
    public string? Squeegee { get; set; }           // BS / BMS / YR …
    public string? Dry { get; set; }                // OVEN / ND / DR / UV
    public double? TemperatureC { get; set; }
    public int? TimeMin { get; set; }               // dry minutes
    public string? Uv { get; set; }                 // mJ/cm² hoặc text
    public double? EmulsionUm { get; set; }
    public string? PlateSize { get; set; }          // "700×950"
    public string? Mesh { get; set; }               // "L120"
    public double? AngleDeg { get; set; }
    public string? PlateCode { get; set; }          // "SP1620-1"
    public int? ControlNo { get; set; }
    public string? Remark { get; set; }
    public string? ExtraJson { get; set; }          // future-proof per-process variance
}

/// <summary>
/// Phase 8 PR #31b — Flexo cutting row (per process). 1:N keyed by
/// SpecPrintId. Mirror SpecHub `flexoData.cuttingRows[i]` 14-field shape
/// (HTML:11707-11722).
///
/// Hai entity flexo (Cutting + Ink) tách riêng vì semantic độc lập: cutting
/// chỉ độc các thông số die cut + lamination + tốc độ máy cắt; ink là per-
/// color UV/IR power + anilox. PrintingRows (substrate/cylinder/tension) fold
/// vào SpecPrint.ExtraJson — ít cần query column-wise + giảm số bảng wide.
/// </summary>
public class SpecFlexoCuttingRow : BaseEntity
{
    public long SpecPrintId { get; set; }
    public SpecPrint? SpecPrint { get; set; }

    public int Seq { get; set; }                    // R7+ row order
    public string? Process { get; set; }            // "FLEXBED CUT", "ROTARY"...
    public string? Lamination { get; set; }
    public string? Size { get; set; }               // "112×95"
    public string? CutterLot { get; set; }
    public string? CutterName { get; set; }
    public int? PcsPerSheet { get; set; }
    public int? CuttingCavity { get; set; }
    public double? PitchMm { get; set; }
    public string? Packing { get; set; }
    public double? PaperSpeed { get; set; }
    public double? CuttingSpeed { get; set; }
    public double? CuttingPressure { get; set; }
    public double? HeadTension { get; set; }
    public double? RollTension { get; set; }
    public string? ExtraJson { get; set; }
}

/// <summary>
/// Phase 8 PR #31b — Flexo ink row (per color). 1:N keyed by SpecPrintId.
/// Mirror SpecHub `flexoData.inkRows[i]` 10-field shape (HTML:11740-11751).
///
/// Tách bảng riêng khỏi <see cref="SpecPrintColor"/> (silkscreen-shape):
/// flexo có anilox + UV/IR power là semantic khác hẳn squeegee/mesh của silk.
/// PR #33 detail sheet sẽ render từ entity rows trực tiếp.
/// </summary>
public class SpecFlexoInkRow : BaseEntity
{
    public long SpecPrintId { get; set; }
    public SpecPrint? SpecPrint { get; set; }

    public int Seq { get; set; }
    public string? Color { get; set; }              // "WHITE", "PANTONE 186C"...
    public string? InkCode { get; set; }            // "HI160", "VI2"
    public string? InkDescription { get; set; }
    public string? Brand { get; set; }              // "CCL MIX", "SEIKO"
    public string? Anilox { get; set; }             // "L120" / "UX59" / anilox volume
    public string? PlateCode { get; set; }          // "SP2387-1"
    public double? Pressure { get; set; }
    public double? UvPowerW { get; set; }
    public double? IrPowerW { get; set; }
    public string? ExtraJson { get; set; }
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
