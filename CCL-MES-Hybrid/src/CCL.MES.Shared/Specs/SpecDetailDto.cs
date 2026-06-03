namespace CCL.MES.Shared.Specs;

/// <summary>
/// P10.5b — Spec detail payload. Mirrors
/// <c>CCL.MES.Application.SpecDetail.SpecDetailDto</c> (Phase 8 PR #31d).
/// The legacy DTO is a class with helper props (StatusDisplay,
/// ComplianceChips, ProductSizeDisplay) that we DO NOT need on the wire —
/// we keep only the raw data fields. Helpers reappear on the client
/// inside <see cref="SpecShowcardVm"/> as a flatten + format step.
/// </summary>
public sealed record SpecDetailItem
{
    // Identity
    public long Id { get; init; }
    public string SpecCode { get; init; } = "";
    public string Title { get; init; } = "";
    public string RevisionCode { get; init; } = "A";
    public SpecRevisionStatus Status { get; init; }
    public string? RefNo { get; init; }
    public string? InspectionLevel { get; init; }
    public string Planner { get; init; } = "UNKNOWN";
    public string ProductCode { get; init; } = "";
    public string ProductName { get; init; } = "";
    public string? CustomerName { get; init; }
    public string ProcessCode { get; init; } = "";
    public bool IsSilkscreen { get; init; }
    public bool IsFlexo { get; init; }

    // Material
    public string? SubstrateType { get; init; }
    public string? SubstrateBrand { get; init; }
    public string? AdhesiveType { get; init; }
    public string? AdhesiveBrand { get; init; }
    public int? ThicknessUm { get; init; }
    public string? MaterialExtraJson { get; init; }

    // Print params
    public int? PrintingCavity { get; init; }
    public double? LengthPitchMm { get; init; }
    public double? ProductSizeWmm { get; init; }
    public double? ProductSizeHmm { get; init; }
    public string? Varnish { get; init; }
    public string? Lamination { get; init; }
    public bool WhiteUnderprint { get; init; }
    public string? PrintExtraJson { get; init; }
    public string? ColorSpecJson { get; init; }

    // Remarks
    public string? RemarksText { get; init; }
    public string? RemarksCutText { get; init; }

    // Silk print rows
    public List<SpecPrintColorRow> PrintColors { get; init; } = new();

    // Flexo rows
    public List<SpecFlexoPrintRow> FlexoPrintRows { get; init; } = new();
    public List<SpecFlexoCuttingRow> FlexoCuttingRows { get; init; } = new();
    public List<SpecFlexoInkRow> FlexoInkRows { get; init; } = new();

    // Revision lineage
    public List<SpecRevisionLineageEntry> Lineage { get; init; } = new();

    // Approval
    public string? ApprovedBy { get; init; }
    public DateTime? ApprovedAt { get; init; }
    public string? ReleasedBy { get; init; }
    public DateTime? ReleasedAt { get; init; }

    // Audit
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
    public List<SpecAuditEntry> AuditEntries { get; init; } = new();
}

public sealed record SpecPrintColorRow(
    int Seq,
    string? Surface,
    string? Color,
    string? InkName,
    string? InkCode,
    string? Maker,
    string? Retarder,
    double? Viscosity,
    double? Speed,
    string? Squeegee,
    string? Dry,
    double? TemperatureC,
    int? TimeMin,
    string? Uv,
    double? EmulsionUm,
    string? PlateSize,
    string? Mesh,
    double? AngleDeg,
    string? PlateCode,
    int? ControlNo,
    string? Remark);

public sealed record SpecFlexoPrintRow(
    int Seq,
    string? Process,
    string? Material,
    string? Thickness,
    string? Size,
    string? Cylinders,
    string? PitchMm,
    string? Speed,
    string? TensionHead,
    string? TensionEnd,
    string? TensionRoll,
    string? PlateCavity,
    string? Tension);

public sealed record SpecFlexoCuttingRow(
    int Seq,
    string? Process,
    string? Lamination,
    string? Size,
    string? CutterLot,
    string? CutterName,
    int? PcsPerSheet,
    int? CuttingCavity,
    double? PitchMm,
    string? Packing,
    double? PaperSpeed,
    double? CuttingSpeed,
    double? CuttingPressure,
    double? HeadTension,
    double? RollTension);

public sealed record SpecFlexoInkRow(
    int Seq,
    string? Color,
    string? InkCode,
    string? InkDescription,
    string? Brand,
    string? Anilox,
    string? PlateCode,
    double? Pressure,
    double? UvPowerW,
    double? IrPowerW);

public sealed record SpecRevisionLineageEntry(
    long Id,
    string RevisionCode,
    string? ChangeSummary,
    DateTime CreatedAt,
    string? CreatedBy);

public sealed record SpecAuditEntry(
    DateTime Timestamp,
    string Action,
    string ActorUsername,
    string ActorRole,
    string? Detail);
