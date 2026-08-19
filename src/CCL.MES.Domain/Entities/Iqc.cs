namespace CCL.MES.Domain.Entities;

/// <summary>
/// Phase 6 Bước 7 — IQC (Incoming Quality Check) phiếu kiểm chất lượng
/// nguyên liệu khi nhập kho.
///
/// Vì sao tách khỏi <see cref="QcInspection"/>:
///   - QcInspection.WorkOrderId là FK BẮT BUỘC tới WorkOrder; IQC chạy
///     TRƯỚC khi có WO (raw mat mới về kho, chưa biết WO nào sẽ dùng).
///   - QcType enum (IPQC/FQC/OQC) thuộc WO-flow; IQC là pre-WO.
///   - QcService.ApproveAsync khi Fail set WO.Status=OnHold — semantically
///     wrong cho IQC (raw mat fail → quarantine raw mat, không phải hold WO).
///
/// FK hybrid: <see cref="RawMaterialId"/> nullable optional + <see cref="PartNo"/>
/// snapshot bắt buộc. Khi catalog có part → set FK + lookup nhanh; khi không
/// → giữ text. Snapshot giữ lịch sử ngay cả khi catalog rename về sau.
///
/// Result reuse <see cref="CCL.MES.Domain.QcResult"/> (Pending/Pass/Fail) —
/// semantically identical với QC.
/// </summary>
public class IqcInspection : BaseEntity
{
    // ── Nhóm phiếu (feat/iqc-module-tabs) — ADDITIVE ────────────
    // Phân loại nguồn nhập: Materials (form đảo-chiều hiện tại) · Chemical ·
    // Tools · Other (3 form placeholder riêng). Default "Materials" nên phiếu
    // legacy + form Materials cũ chạy nguyên. Lưu dạng string (mirror pattern
    // CurrentStep / MatchStatus) — KHÔNG thêm enum Domain mới để giữ migration
    // additive tối thiểu. Whitelist giá trị ở <see cref="IqcGroup"/>.
    public string Group { get; set; } = IqcGroup.Materials;

    // ── Liên kết RawMaterial (hybrid FK) ─────────────────────────
    public long? RawMaterialId { get; set; }
    public RawMaterial? RawMaterial { get; set; }
    public string PartNo { get; set; } = "";

    // ── Batch + nhập kho ────────────────────────────────────────
    public string BatchNumber { get; set; } = "";
    public string? LotNumber { get; set; }
    public DateTime ReceivedDate { get; set; }
    public string? SupplierName { get; set; }
    public double Quantity { get; set; }
    public string? UomQty { get; set; }

    // ── IQC ticket (feat/iqc-ticket) — 6 field additive ─────────
    // Số phiếu do server sinh (IQC-<yyMMdd>-<STT4>), duy nhất NOCASE qua
    // filtered unique index. 3 dòng legacy để null → không dính index.
    public string? ReceiptNo { get; set; }

    // Code IFS operator nhập/scan. Snapshot text luôn giữ (quyết định #2:
    // không match vẫn lưu, RawMaterialId=null). RawMaterialId (đã có ở trên)
    // là FK resolve; CodeIfs là bằng chứng operator đã gõ gì.
    public string? CodeIfs { get; set; }
    public string? MakerName { get; set; }
    public DateTime? ManufactureDate { get; set; }

    // PA-A (quyết định #1): CACHE mô tả lúc tạo phiếu = bằng chứng bất biến.
    // RawMaterial.PartDescription có thể bị rename về sau; phiếu giữ bản chụp.
    public string? MaterialDescription { get; set; }
    public string? IfsDescription { get; set; }

    // ── Inspection ──────────────────────────────────────────────
    public string? InspectorId { get; set; }
    public int SampleSize { get; set; }
    public QcResult Result { get; set; } = QcResult.Pending;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public List<IqcResultDetail> Details { get; set; } = new();
}

/// <summary>
/// feat/iqc-module-tabs — nhóm phiếu IQC. Giá trị canonical dạng string
/// (không đổi số enum vì lưu chuỗi), whitelist tường minh để service validate.
/// Additive: chỉ THÊM giá trị cuối, KHÔNG đổi nghĩa giá trị đã có.
/// </summary>
public static class IqcGroup
{
    public const string Materials = "Materials";
    public const string Chemical = "Chemical";
    public const string Tools = "Tools";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Materials, Chemical, Tools, Other,
    };

    /// <summary>Chuẩn hoá + kiểm hợp lệ. Rỗng/không rõ → Materials (backward
    /// compat: form cũ không khai group). So khớp không phân biệt hoa thường,
    /// trả về dạng canonical.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Materials;
        foreach (var g in All)
            if (string.Equals(g, raw.Trim(), StringComparison.OrdinalIgnoreCase))
                return g;
        return Materials;
    }

    public static bool IsValid(string? raw) =>
        !string.IsNullOrWhiteSpace(raw) &&
        All.Any(g => string.Equals(g, raw.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Phase 6 Bước 7 — chi tiết từng item kiểm trong 1 IqcInspection.
/// Schema mirror <see cref="QcResultDetail"/> nhưng table riêng (FK đơn nghĩa).
/// </summary>
public class IqcResultDetail : BaseEntity
{
    public long IqcInspectionId { get; set; }
    public string ItemName { get; set; } = "";
    public string? MeasuredValue { get; set; }
    public bool Pass { get; set; }
    public string? DefectCode { get; set; }
    public int Qty { get; set; }
}
