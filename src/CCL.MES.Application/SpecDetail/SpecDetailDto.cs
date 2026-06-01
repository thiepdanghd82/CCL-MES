using System.Globalization;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;

namespace CCL.MES.Application.SpecDetail;

/// <summary>
/// Phase 8 PR #31d — Full detail sheet payload cho EngineerSpecDetail.razor
/// + PdfSpecSheetExporter. Server query 1 lần Include full graph; client +
/// PDF builder bind trực tiếp (KHÔNG re-fetch per section per bài học #27).
///
/// Silk vs flexo phân biệt qua <see cref="IsSilkscreen"/> / <see cref="IsFlexo"/>
/// (derive từ ProcessCode). Render path branch tại Razor / PDF caller.
/// </summary>
public class SpecDetailDto
{
    // ── Identity ──────────────────────────────────────────────────────────
    public long Id { get; set; }
    public string SpecCode { get; set; } = "";
    public string Title { get; set; } = "";
    public string RevisionCode { get; set; } = "A";
    public ProductRevisionStatus Status { get; set; }
    public string? RefNo { get; set; }
    public string? InspectionLevel { get; set; }
    public string Planner { get; set; } = "UNKNOWN";
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public string? CustomerName { get; set; }
    public string ProcessCode { get; set; } = "";

    /// <summary>True khi ProcessCode SILKSCREEN / INDIGO / INDIGO_PRIMER.</summary>
    public bool IsSilkscreen { get; set; }

    /// <summary>True khi ProcessCode FLEXO.</summary>
    public bool IsFlexo { get; set; }

    // ── Material (silk + flexo chia chung) ────────────────────────────────
    public string? SubstrateType { get; set; }
    public string? SubstrateBrand { get; set; }
    public string? AdhesiveType { get; set; }
    public string? AdhesiveBrand { get; set; }
    public int? ThicknessUm { get; set; }
    /// <summary>JSON `{material_size, lamination_tape, lamination_size, lamination_cavity}` từ PR #31a.</summary>
    public string? MaterialExtraJson { get; set; }

    // ── Print params (silk) + flexo Version ───────────────────────────────
    public int? PrintingCavity { get; set; }
    public double? LengthPitchMm { get; set; }
    public double? ProductSizeWmm { get; set; }   // PR #31d
    public double? ProductSizeHmm { get; set; }   // PR #31d
    public string? Varnish { get; set; }
    public string? Lamination { get; set; }
    public bool WhiteUnderprint { get; set; }
    /// <summary>JSON `flexo_print_rows` (flexo); silk ColorSpecJson (legacy fallback) tách field riêng.</summary>
    public string? PrintExtraJson { get; set; }
    public string? ColorSpecJson { get; set; }

    // ── Remarks (PR #31d) ─────────────────────────────────────────────────
    public string? RemarksText { get; set; }
    public string? RemarksCutText { get; set; }

    // ── Silk print rows (PR #31a) — 20 fields per color ───────────────────
    public List<SpecPrintColorRow> PrintColors { get; set; } = new();

    // ── Flexo rows (PR #31b) ──────────────────────────────────────────────
    public List<FlexoPrintRow> FlexoPrintRows { get; set; } = new();
    public List<FlexoCuttingRow> FlexoCuttingRows { get; set; } = new();
    public List<FlexoInkRow> FlexoInkRows { get; set; } = new();

    // ── Revision lineage (walk ParentRevisionId DESC max 6) ───────────────
    public List<RevisionLineageEntry> Lineage { get; set; } = new();

    // ── Approval (Option A — render-only, ApprovedBy/At maps to R&D Confirmed) ──
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public List<CCL.MES.Application.SpecAuditEntry> AuditEntries { get; set; } = new();

    /// <summary>Status 5→3 display (mirror PR #31c SpecListColumns.StatusDisplay).</summary>
    public string StatusDisplay => Status switch
    {
        ProductRevisionStatus.Draft or ProductRevisionStatus.InReview     => "DRAFT",
        ProductRevisionStatus.Approved or ProductRevisionStatus.Released  => "APPROVED",
        ProductRevisionStatus.Superseded                                  => "Superseded",
        _                                                                  => Status.ToString(),
    };

    /// <summary>Q13 — compliance derive 3-chip (HSF fixed + Spec inspection + RoHS fallback).</summary>
    public IReadOnlyList<string> ComplianceChips => new[]
    {
        "HSF strict control",
        string.IsNullOrWhiteSpace(InspectionLevel) ? "Spec A" : $"Spec {InspectionLevel}",
        "RoHS Compliance",
    };

    /// <summary>"W × H mm" format hoặc "—" cho NULL.</summary>
    public string ProductSizeDisplay
    {
        get
        {
            if (ProductSizeWmm is null && ProductSizeHmm is null) return "—";
            var w = ProductSizeWmm?.ToString("N1", CultureInfo.InvariantCulture) ?? "—";
            var h = ProductSizeHmm?.ToString("N1", CultureInfo.InvariantCulture) ?? "—";
            return $"{w} × {h} mm";
        }
    }
}

/// <summary>Silk print row — 20 fields per color (PR #31a SpecPrintColor mirror).</summary>
public record SpecPrintColorRow(
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

/// <summary>Flexo printing row (12 fields per process) — từ SpecPrint.ExtraJson `flexo_print_rows`.</summary>
public record FlexoPrintRow(
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

/// <summary>Flexo cutting row (14 fields per process) — SpecFlexoCuttingRow.</summary>
public record FlexoCuttingRow(
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

/// <summary>Flexo ink row (10 fields per color) — SpecFlexoInkRow.</summary>
public record FlexoInkRow(
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

/// <summary>Revision lineage entry — walk ParentRevisionId chain DESC.</summary>
public record RevisionLineageEntry(
    long Id,
    string RevisionCode,
    string? ChangeSummary,
    DateTime CreatedAt,
    string? CreatedBy);

