namespace CCL.MES.Application.Services;

/// <summary>Một dòng ledger Excel (Roll/PCS/Chem/Tool).</summary>
public sealed record IqcHistoryLedgerRow(
    string Sheet,
    int ExcelRow,
    int? Stt,
    DateTime InspectedAt,
    string? SupplierName,
    string? CodeIfs,
    string? MotherCode,
    string? MaterialName,
    string? PoNumber,
    double Quantity,
    string? Uom,
    string? FinalJudgment,
    string? Inspector,
    IqcHistoryLedgerChecks? Checks = null);

/// <summary>
/// Khối kiểm tra đã đọc từ cột Excel (Roll S–BN / PCS P–AP).
/// Chem/Tool = null (phase sau).
/// </summary>
public sealed record IqcHistoryLedgerChecks(
    // ── Điều kiện đóng gói ──────────────────────────────────────
    string? WarehouseInDate,
    string? ExpiryText,
    string? Pefc,
    string? PefcLevel,
    string? PackagingSpec,       // PCS: quy cách
    bool? PackagingPass,
    string? PackagingInspector,
    // ── Visual ──────────────────────────────────────────────────
    int? VisualSampleQty,
    IReadOnlyList<IqcLedgerDefectCell> VisualDefects,
    bool? VisualPass,
    string? VisualInspector,
    // ── Dimension ───────────────────────────────────────────────
    double? WidthNominal,
    double? WidthLow,
    double? WidthUp,
    IReadOnlyList<double?> WidthSamples,       // 5 — Roll: Độ rộng; PCS: parsed
    IReadOnlyList<string?> WidthSampleTexts,   // PCS "rộng x dài" raw
    bool? WidthPass,
    string? ThicknessSpec,
    IReadOnlyList<double?> ThicknessSamples,   // 5
    bool? ThicknessPass,
    string? DimensionInspector,
    // ── Functional (Roll BA–BC) — Adhesive và/hoặc Hardness ─────
    string? FuncSpec,
    bool? FuncPass,
    string? FuncInspector,
    // ── Lab L-a-b (Roll BG–BN) — chỉ khi có dữ liệu ─────────────
    string? LabSpec,
    IReadOnlyList<double?> LabSheets,          // 5
    bool? LabPass,
    string? LabInspector);

public readonly record struct IqcLedgerDefectCell(string ItemKey, int? Count);

/// <summary>Kết quả một lần nạp ledger → <c>IqcInspections</c> (+ optional details).</summary>
public readonly record struct IqcHistoryLedgerImportResult(
    int RowsRead,
    int RowsSkippedNoJudgment,
    int RowsSkippedPcsContinuation,
    int Inserted,
    int AlreadyPresent,
    int DetailsUpserted = 0);
