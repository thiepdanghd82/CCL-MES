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
