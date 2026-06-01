namespace CCL.MES.Application.SpecImport;

/// <summary>
/// Phase 8 PR #31a — DTO shape sau khi parse 1 file xlsx silkscreen spec.
/// Mirror SpecHub `parseXlsxSilkscreen` return shape (`spechub-prototype.html`
/// HTML:11590-11640). Parser layer SẠCH — KHÔNG chạm DB; chỉ trả về cấu trúc
/// dữ liệu thuần để SpecImportService quyết định save vs preview.
///
/// Per Q11 — LETTER/INDIGO/DIECUT chưa port; chỉ silkscreen + (PR #31b) flexo.
/// Khi planner ≠ silkscreen runtime fallback silkscreen layout, warning được
/// gắn vào <see cref="Warnings"/>.
/// </summary>
public class ParsedSpecDto
{
    public string Category { get; set; } = "silkscreen";

    // ── Header ────────────────────────────────────────────────────────────
    public string RefNo { get; set; } = "";
    public string InspectionLevel { get; set; } = "A";
    public string ApprovalStamp { get; set; } = "Approved";
    public string Compliance { get; set; } = "RoHS Compliance";

    // ── Product info ──────────────────────────────────────────────────────
    public string Customer { get; set; } = "";
    public string PartNo { get; set; } = "";
    public string PartName { get; set; } = "";
    public string MaterialType { get; set; } = "";
    public string MaterialSize { get; set; } = "";
    public string LaminationTape { get; set; } = "";
    public string LaminationSize { get; set; } = "";
    public string LaminationCavity { get; set; } = "";

    // ── Print params (silkscreen) ─────────────────────────────────────────
    public int? PrintingCavity { get; set; }
    public double? LengthPitchMm { get; set; }
    public double? ProductSizeW { get; set; }
    public double? ProductSizeH { get; set; }
    public double? Diameter { get; set; }

    // ── Print rows (silkscreen 20 fields per color) ───────────────────────
    public List<ParsedPrintColorDto> PrintRows { get; set; } = new();

    /// <summary>Remarks blob — long text từ "Ghi chú/Remarks" section.
    /// Silk: single column. Flexo: combined "left | right" for legacy callers;
    /// PR #31d use <see cref="RemarksLeft"/> + <see cref="RemarksRight"/> riêng.</summary>
    public string Remarks { get; set; } = "";

    // PR #31d — tách remarks 2-col cho flexo (silk = chỉ RemarksLeft).
    /// <summary>Flexo print remarks (silk = main remarks).</summary>
    public string? RemarksLeft { get; set; }

    /// <summary>Flexo cut remarks (silk = null).</summary>
    public string? RemarksRight { get; set; }

    /// <summary>Warnings/skipped rows surfaced ở preview UI (non-fatal).</summary>
    public List<string> Warnings { get; set; } = new();

    // ── PR #31b — Flexo-specific (silkscreen = empty) ────────────────────────
    /// <summary>Flexo "version" field từ R4 C21 (silkscreen = "").</summary>
    public string? Version { get; set; }
    public List<ParsedFlexoPrintRowDto> FlexoPrintRows { get; set; } = new();
    public List<ParsedFlexoCuttingRowDto> FlexoCuttingRows { get; set; } = new();
    public List<ParsedFlexoInkRowDto> FlexoInkRows { get; set; } = new();
}

/// <summary>
/// Per-color silkscreen print row — 20 fields mirror SpecHub `printRows[i]`.
/// </summary>
public class ParsedPrintColorDto
{
    public int Seq { get; set; }
    public string? Surface { get; set; }
    public string? Color { get; set; }
    public string? InkName { get; set; }
    public string? InkCode { get; set; }
    public string? Maker { get; set; }
    public string? Retarder { get; set; }
    public double? Viscosity { get; set; }
    public double? Speed { get; set; }
    public string? Squeegee { get; set; }
    public string? Dry { get; set; }
    public double? TemperatureC { get; set; }
    public int? TimeMin { get; set; }
    public string? Uv { get; set; }
    public double? EmulsionUm { get; set; }
    public string? PlateSize { get; set; }
    public string? Mesh { get; set; }
    public double? AngleDeg { get; set; }
    public string? PlateCode { get; set; }
    public int? ControlNo { get; set; }
    public string? Remark { get; set; }
}

/// <summary>
/// PR #31b — Flexo printing rows (12 fields per process). Fold vào
/// SpecPrint.ExtraJson per Q3 — operator ít cần column-wise query.
/// </summary>
public class ParsedFlexoPrintRowDto
{
    public int Seq { get; set; }
    public string? Process { get; set; }
    public string? Material { get; set; }
    public string? Thickness { get; set; }
    public string? Size { get; set; }
    public string? Cylinders { get; set; }
    public string? PitchMm { get; set; }
    public string? Speed { get; set; }
    public string? TensionHead { get; set; }
    public string? TensionEnd { get; set; }
    public string? TensionRoll { get; set; }
    public string? PlateCavity { get; set; }
    public string? Tension { get; set; }
}

/// <summary>
/// PR #31b — Flexo cutting rows (14 fields per process). Persist vào
/// SpecFlexoCuttingRow entity.
/// </summary>
public class ParsedFlexoCuttingRowDto
{
    public int Seq { get; set; }
    public string? Process { get; set; }
    public string? Lamination { get; set; }
    public string? Size { get; set; }
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
}

/// <summary>
/// PR #31b — Flexo ink rows (10 fields per color). Persist vào SpecFlexoInkRow
/// entity (tách khỏi SpecPrintColor silk-shape per Q3).
/// </summary>
public class ParsedFlexoInkRowDto
{
    public int Seq { get; set; }
    public string? Color { get; set; }
    public string? InkCode { get; set; }
    public string? InkDescription { get; set; }
    public string? Brand { get; set; }
    public string? Anilox { get; set; }
    public string? PlateCode { get; set; }
    public double? Pressure { get; set; }
    public double? UvPowerW { get; set; }
    public double? IrPowerW { get; set; }
}

/// <summary>
/// PR #31a Q12 — preview shape returned from `SpecImportService.PreviewAsync`.
/// Modal binds trực tiếp (KHÔNG re-fetch by id; bài học hotfix PR #27).
/// </summary>
public class SpecImportPreviewDto
{
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string PlannerCategory { get; set; } = "silkscreen";
    public bool ParseOk { get; set; }
    public string? ParseError { get; set; }
    public ParsedSpecDto? Parsed { get; set; }

    /// <summary>
    /// Duplicate detection — match by RefNo trên existing ProductRevisions
    /// (non-trashed). Khi true, Save sẽ reject với error (PR #31a default).
    /// Lifecycle Replace/Upgrade defer PR sau.
    /// </summary>
    public bool DuplicateRefNo { get; set; }
    public long? DuplicateRevisionId { get; set; }
    public string? DuplicateSpecCode { get; set; }

    /// <summary>Aggregated warnings (parser + product lookup).</summary>
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// PR #31a Q16 — Save result + audit detail.
/// </summary>
public class SpecImportSaveResult
{
    public long ProductRevisionId { get; set; }
    public long ProductId { get; set; }
    public string SpecCode { get; set; } = "";
    public string RefNo { get; set; } = "";
    public int RowsParsed { get; set; }
    public bool CreatedNewProduct { get; set; }
    public List<string> Warnings { get; set; } = new();
}
