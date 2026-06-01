using CCL.MES.Domain;
using CCL.MES.Domain.StateMachine;

namespace CCL.MES.Application;

public class CreateWoRequest
{
    public string WoNo { get; set; } = "";
    public long CustomerId { get; set; }
    public long ProductId { get; set; }
    public string ProductName { get; set; } = "";
    // Phase 8 PR #28 — was SpecVersionId; refactored to ProductRevisionId after
    // Spec → ProductRevision clean rewrite (SpecHub revision-is-truth model).
    public long? ProductRevisionId { get; set; }
    public string? MachineCode { get; set; }
    public string? MachineName { get; set; }
    public int TargetQty { get; set; }
    public string? Uom { get; set; }
}

public class UpdateFlagsRequest
{
    public bool? MaterialsReady { get; set; }
    public bool? SetupConfirmed { get; set; }
    public bool? RohsOk { get; set; }
    public int? ProducedQty { get; set; }
}

// Phase 8 PR #28 — kept name `CreateSpecRequest` for backward compat with the
// EngineerSpec.razor → CreateSpecModal.razor surface; semantics rewired to
// ProductRevision (rev A) + SpecPrint with parameters folded into ColorSpecJson
// as a JSON array. Full editor form (per-sibling-table inputs for Material/
// Diecut/Finishing) đẩy PR #29.
public class CreateSpecRequest
{
    public long ProductId { get; set; }
    public string SpecCode { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Process code lookup vào ProcessCatalog (default 'SILKSCREEN' khi null).</summary>
    public string? ProcessCode { get; set; }

    public List<SpecParamDto> Parameters { get; set; } = new();
}

public class SpecParamDto
{
    public string ParamName { get; set; } = "";
    public string? Nominal { get; set; }
    public string? TolMin { get; set; }
    public string? TolMax { get; set; }
    public string? Uom { get; set; }
    public bool IsCritical { get; set; }
}

// Phase 8 PR #28 (extended PR #30) — lightweight grid projection cho
// EngineerSpec.razor. PR #30 SpecHub list-view parity ADD 9 fields:
// CustomerName / RefNo / InspectionLevel / NumColors / Cavity / PitchMm /
// Planner (derived) / LastUpdatedAt / LastUpdatedBy.
//
// Pattern: pre-flatten thay vì kéo full graph qua serializer (DTO record
// immutable + nullable lúc nào cũng có nghĩa). 14-col grid SpecHub map:
//   # / Planner / REF NO / Customer / Part No / Part Name / Colors /
//   Cavity / Pitch / Spec / Status / Rev / Rev Date / By
// (Cột # render từ row index, không cần field; 13 cột còn lại = 13 fields.)
public record ProductRevisionListItem(
    long Id,
    string SpecCode,
    string Title,
    string RevisionCode,
    CCL.MES.Domain.ProductRevisionStatus Status,
    DateTime? EffectiveFrom,
    string? ApprovedBy,
    string ProductCode,
    string ProductName,
    string? ProcessCode,
    // ── Phase 8 PR #30 — 9 fields mới cho list-view parity SpecHub ──────────
    string? CustomerName,        // SpecHub col `Customer` — join Product.Customer.Name
    string? RefNo,               // SpecHub col `REF NO` — customer-facing reference
    string? InspectionLevel,     // SpecHub col `Spec` — quality grade e.g. A166
    int NumColors,               // SpecHub col `Colors` — SpecPrint.NumColors
    int? Cavity,                 // SpecHub col `Cavity` — SpecPrint.Cavity
    double? PitchMm,             // SpecHub col `Pitch` — SpecPrint.PitchMm (mm)
    string? Planner,             // SpecHub col `Planner` — DERIVED từ ProcessCode (SILK/FLEXO/LETTER/INDIGO/DIECUT/UNKNOWN)
    DateTime? LastUpdatedAt,     // SpecHub col `Rev Date` — UpdatedAt fallback CreatedAt
    string? LastUpdatedBy);      // SpecHub col `By` — UpdatedBy fallback CreatedBy

// ── Phase 8 PR #29 — SpecDetailModal content + audit query DTOs ────────────────
// Modal Identity render trực tiếp từ ProductRevisionListItem (đã có ở grid).
// Spec content + Audit trail là query phụ — tách section + try-catch + error
// banner inline thay vì freeze circuit (bài học hotfix PR #27).

/// <summary>
/// Spec content cluster: 4 sibling specs (Material/Print/Diecut/Finishing) +
/// audit stamps (Created/Updated/Approved). Tất cả nullable — section nào
/// rỗng render "—".
/// </summary>
public record SpecContentDto(
    long RevisionId,
    SpecMaterialDto? Material,
    SpecPrintDto? Print,
    SpecDiecutDto? Diecut,
    SpecFinishingDto? Finishing,
    DateTime CreatedAt,
    string? CreatedBy,
    DateTime? UpdatedAt,
    string? UpdatedBy,
    string? ApprovedBy,
    DateTime? ApprovedAt);

public record SpecMaterialDto(
    string? SubstrateType,
    string? SubstrateBrand,
    int? ThicknessUm,
    string? LinerType,
    string? AdhesiveType,
    string? AdhesiveBrand,
    string? ExtraJson);

public record SpecPrintDto(
    string? ProcessCode,
    int NumColors,
    string? ColorSpecJson,
    string? Varnish,
    string? Lamination,
    bool WhiteUnderprint,
    string? ExtraJson);

public record SpecDiecutDto(
    string? CutProcessCode,
    string? DieId,
    string? DieType,
    double? WidthMm,
    double? LengthMm,
    double? CornerRadiusMm,
    int? KissCutDepthUm,
    double? BleedMm,
    string? ExtraJson);

public record SpecFinishingDto(
    string? OutputForm,
    int? LabelsPerRoll,
    double? CoreDiameterMm,
    string? WindingDirection,
    string? FinishingProcessesJson,
    string? ExtraJson);

/// <summary>
/// AuditLog row dùng cho timeline section (Q9: query AuditLog, KHÔNG thêm
/// field mới). Sort Timestamp DESC ở caller.
/// </summary>
public record SpecAuditEntry(
    DateTime Timestamp,
    string Action,
    string ActorUsername,
    string ActorRole,
    string? Detail);

public class CreateQcRequest
{
    public long WorkOrderId { get; set; }
    public QcType Type { get; set; }
    public string? InspectorId { get; set; }
    public int SampleSize { get; set; }
    public List<QcDetailDto> Details { get; set; } = new();
}

public class QcDetailDto
{
    public string ItemName { get; set; } = "";
    public string? MeasuredValue { get; set; }
    public bool Pass { get; set; }
    public string? DefectCode { get; set; }
    public int Qty { get; set; }
}

// Phase 5 — ErrorCode replaces the prior free-form Error string. The Web
// layer maps the enum to a localized resource key via WoErrorKeys; the API
// serialises the enum as its name (e.g. "RequiresSpecAndMaterials") thanks
// to JsonStringEnumConverter registered in Program.cs.
public record AdvanceResult(bool Ok, WoErrorCode? ErrorCode, string CurrentStep);

// Phase 7 hạng mục 4 — lightweight Product projection cho CreateSpecModal
// dropdown. KHÔNG include Customer hay full Product graph để tránh kéo
// dữ liệu thừa qua serializer.
public record ProductDropdownItem(long Id, string ProductCode, string Name);

// Phase 8 — Work Center context-menu actions (Open/Edit/Copy/Toggle).
// DetailDto trả về entity row + usage stats derived từ RoutingOperations
// (count + distinct parts + avg setup/run + top-5 parts). KHÔNG include
// ProductionLog vì Machine entity chưa link với WorkCenter — defer.
public record WorkCenterDetailDto(
    CCL.MES.Domain.Entities.WorkCenter Row,
    WorkCenterUsageStats Usage);

public record WorkCenterUsageStats(
    int OpCount,
    int DistinctPartCount,
    double? AvgMachSetup,
    double? AvgLaborSetup,
    double? AvgMachRun,
    List<TopPartUsage> TopParts);

public record TopPartUsage(string PartNo, int OpCount);

// Edit + Copy reuse cùng request shape. Copy = Edit với srcId tham chiếu
// để pre-fill, server tạo row mới với Code mới (NewCode field overrides
// Code khi non-null trong CopyAsync; Edit thì Code stays).
public record UpdateWorkCenterRequest(
    string Code,
    string Description,
    string? Area,
    double? IdealSpeedPcsH,
    string? ShiftPattern,
    bool? Active);
