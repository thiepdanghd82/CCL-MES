namespace CCL.MES.Hybrid.Client.Qms;

// ─────────────────────────────────────────────────────────────────────────
// QA / mini-QMS — presentation view-models + STATIC MOCK DATA (scaffold
// phase). These are UI view-models, NOT API DTOs / domain entities: no
// persistence, no wire contract. When the real data lands, a service fills
// the same shapes and the Razor bindings stay unchanged.
// ─────────────────────────────────────────────────────────────────────────

public enum QmsVerdict { None, Pass, Ng }

public enum QmsLotDecision { None, Accept, Reject }

/// <summary>One numbered stepper step — chrome keys resolved via T().</summary>
public sealed record QmsStep(int Num, string LabelKey, string EnKey);

/// <summary>IQC HSF document row.</summary>
public sealed record QmsHsfDoc(
    string Name, string No, string Issue, string Expiry,
    string ModifiedBy, string ModifiedAt, bool Valid);

/// <summary>IPQC material-reconciliation row (system vs actual at machine).</summary>
public sealed record QmsMatMatch(
    string SystemMaterial, string MaterialCode, string SourceLot,
    string ActualAtMachine, QmsVerdict Verdict);

/// <summary>Shared inspection-check row (IPQC / OQC) with a PASS/NG verdict.</summary>
public sealed record QmsCheckRow(
    string Item, string? Process, string Method, string Spec,
    string Result, QmsVerdict Verdict);

/// <summary>OQC auto-trace field (IPQC → IQC lineage).</summary>
public sealed record QmsTraceField(string LabelKey, string Value, string Sub);

/// <summary>Header key/value field (receipt · material · supplier …).</summary>
public sealed record QmsHeaderField(string LabelKey, string Value, string? Sub = null);

/// <summary>Defect chip (code + label) for the O/I/P defect pickers.</summary>
public sealed record QmsDefect(string Code, string Label);

/// <summary>iCRA request row.</summary>
public sealed record QmsIcraRow(
    string Code, string Product, string Reason, string Opened);

/// <summary>Static mock content mirroring the QA mini-QMS reference.</summary>
public static class QmsMock
{
    // feat/qms-topbar-breadcrumb: the per-module QmsTopBarVm statics were
    // removed with QmsModuleTopBar — module identity now renders as a
    // breadcrumb in the main app top-bar (TopBar.razor), driven by route.

    public static readonly QmsStep[] IqcSteps =
    {
        new(1, "qms.step.hsf", "qms.step.hsf.en"),
        new(2, "qms.step.packaging", "qms.step.packaging.en"),
        new(3, "qms.step.visual", "qms.step.visual.en"),
        new(4, "iqc.step.dimension", "iqc.step.dimension.en"),
        new(5, "qms.step.functional", "qms.step.functional.en"),
        new(6, "qms.step.defect", "qms.step.defect.en"),
        new(7, "qms.step.history", "qms.step.history.en"),
    };

    public static readonly QmsStep[] ProcessSteps =
    {
        new(1, "qms.step.visual", "qms.step.visual.en"),
        new(2, "qms.step.dimension", "qms.step.dimension.en"),
        new(3, "qms.step.functional", "qms.step.functional.en"),
    };

    public static readonly QmsHeaderField[] IqcHeader =
    {
        new("qms.f.receipt", "IQC-260809-007"),
        new("qms.f.material", "Keo AB-200"),
        new("qms.f.supplier", "Đại Phát"),
        new("qms.f.inspector", "Trần B. Ngọc"),
    };

    public static readonly QmsHsfDoc[] Hsf =
    {
        new("TDS — Technical data sheet", "TDS-AB200-R4", "12/01/2026", "12/01/2027", "Trần Bích Ngọc", "12/01/2026 · IQC Leader", true),
        new("MSDS", "MSDS-AB200-R2", "03/03/2025", "03/03/2026", "Nguyễn Văn Phúc", "03/03/2025 · Member", false),
        new("RoHS", "RH-2026-118", "20/02/2026", "20/02/2027", "Trần Bích Ngọc", "20/02/2026 · IQC Leader", true),
        new("REACH", "RC-2025-902", "15/09/2025", "15/09/2026", "Lê Thị Hạnh", "15/09/2025 · Member", true),
        new("ISO 9001 — NCC", "ISO-DP-2024", "08/06/2024", "08/06/2027", "Trần Bích Ngọc", "08/06/2024 · IQC Leader", true),
    };

    public const string IpqcLot = "LOT-260809-M03-014";
    public static readonly QmsHeaderField[] IpqcHeader =
    {
        new("qms.f.product", "SP-KX120"),
        new("qms.f.machine", "M03 — Ép nhựa"),
        new("qms.f.operator", "Lê Văn Hùng"),
    };

    public static readonly QmsMatMatch[] IpqcMaterials =
    {
        new("Hạt nhựa PP trắng", "NVL-PP-W01", "IQC-260807-021", "PP trắng · lô 260807", QmsVerdict.None),
        new("Keo AB-200", "NVL-KEO-AB2", "IQC-260809-007", "Keo AB-200 · lô 260809", QmsVerdict.None),
        new("Mực in UV xanh", "NVL-UV-B03", "IQC-260731-014", "UV xanh lô 260722 · lệch", QmsVerdict.Ng),
        new("Màng PE bọc", "NVL-PE-C02", "IQC-260805-009", "Màng PE · lô 260805", QmsVerdict.None),
    };

    public static readonly QmsCheckRow[] IpqcChecks =
    {
        new("Bề mặt sản phẩm", "Ép nhựa", "Mắt thường · đèn 800 lux", "Không loang, không xước", "Loang nhẹ", QmsVerdict.Ng),
        new("Màu sắc", "Ép nhựa", "So bảng màu", "ΔE ≤ 1.5", "0.9", QmsVerdict.Pass),
        new("Bavia mép", "Cắt biên", "Mắt thường", "≤ 0.1 mm", "0.06", QmsVerdict.Pass),
        new("Độ bóng", "Ép nhựa", "Gloss meter 60°", "82 ± 4 GU", "83.5", QmsVerdict.Pass),
    };

    public const string OqcLot = "LOT-260809-M03-014";
    public static readonly QmsTraceField[] OqcTrace =
    {
        new("qms.f.product", "SP-KX120", "Vỏ hộp KX 120mm"),
        new("qms.f.machine", "M03", "Ép nhựa · ca A"),
        new("qms.f.operator", "Lê Văn Hùng", "MSNV 0412"),
        new("qms.f.ipqcfirst", "NG 1/4", "Loang bề mặt · P1"),
    };

    public static readonly QmsCheckRow[] OqcChecks =
    {
        new("Ngoại quan tổng thể", null, "AQL 2.5 · mắt thường", "Không lỗi nhóm A", "4 pcs loang", QmsVerdict.Ng),
        new("Kích thước tổng", null, "Thước cặp", "120.0 ± 0.3 mm", "120.12", QmsVerdict.Pass),
        new("Lắp ráp thử", null, "Gá kiểm chuyên dùng", "Lắp trơn, không kẹt", "Đạt", QmsVerdict.Pass),
        new("Nhãn & bao bì", null, "Đối chiếu PO", "Đúng mã, đúng SL", "Đúng", QmsVerdict.Pass),
    };

    public const int OqcLotQty = 2400;
    public const int OqcSampleQty = 32;
    public const int OqcNgQty = 4;
    public const string OqcProduct = "SP-KX120";
    public const string OqcNote = "Loang xuất hiện trên 4/32 pcs kiểm mẫu, tập trung ở khoang 2 của khuôn.";

    public static readonly QmsDefect[] OqcDefects =
    {
        new("O1", "Loang"), new("O4", "Bavia"), new("O7", "Sai kích thước"),
    };

    public static readonly QmsIcraRow[] Icra =
    {
        new("ICRA-260809-01", "SP-KX120", "OQC reject · 4 pcs loang", "09/08/2026 · 14:25"),
    };
}
