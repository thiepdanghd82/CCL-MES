using System.ComponentModel.DataAnnotations;
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

    /// <summary>Nhãn tự do của bản ghi cũ (trước P12). GIỮ LẠI — không xoá dữ
    /// liệu lịch sử. Bản ghi mới dùng <see cref="LabelVi"/>.</summary>
    public string ItemName { get; set; } = "";

    public string? MeasuredValue { get; set; }

    /// <summary>
    /// <c>null</c> = <b>CHƯA KIỂM</b> · <c>true</c> = đạt · <c>false</c> = không đạt.
    ///
    /// <para>Trước P12 đây là <c>bool</c> không nullable, vì hạng mục chỉ được
    /// tạo KÈM kết quả. Từ khi materialize sẵn 13–21 hạng mục lúc mở ticket,
    /// mặc định <c>false</c> sẽ hiện <b>mọi hạng mục là NG</b> — tuyên bố cả lô
    /// không đạt mà không ai bấm gì. Nullable là cách nhỏ nhất để có trạng thái
    /// thứ ba mà không đụng 7 bản ghi cũ (chúng giữ nguyên true/false).</para>
    /// </summary>
    public bool? Pass { get; set; }

    public string? DefectCode { get; set; }
    public int Qty { get; set; }

    // ── P12 — BẰNG CHỨNG ĐÓNG BĂNG lúc mở ticket ────────────────────────
    // Đóng băng cả hai ngôn ngữ ngay tại thời điểm tạo, đúng Nguyên tắc IV:
    // sửa master data về sau KHÔNG hồi tố hồ sơ đã ký. Cùng khuôn với
    // WoIpqcCheckItem (L60) và WoQcChecks.ProfileSnapshotJson (L62).

    /// <summary>Mã hạng mục thư viện — <c>NL-01</c> · <c>NQ-02</c>… Null với
    /// bản ghi cũ nhập tay.</summary>
    [MaxLength(16)] public string? ItemKey { get; set; }

    /// <summary>Thứ tự tiêu chí trong cùng (spec, hạng mục) — xem
    /// <see cref="IqcSpecItem.Seq"/>.</summary>
    public int Seq { get; set; } = 1;

    /// <summary>Spec đã dùng để dựng. Null ⇒ dựng từ ma trận tiêu chuẩn.</summary>
    [MaxLength(32)] public string? SpecNo { get; set; }

    [MaxLength(8)] public string? GroupCode { get; set; }
    [MaxLength(64)] public string? GroupLabelVi { get; set; }
    [MaxLength(64)] public string? GroupLabelEn { get; set; }
    [MaxLength(256)] public string? LabelVi { get; set; }
    [MaxLength(256)] public string? LabelEn { get; set; }
    [MaxLength(1024)] public string? AcceptanceVi { get; set; }
    [MaxLength(1024)] public string? AcceptanceEn { get; set; }
    [MaxLength(512)] public string? MethodVi { get; set; }
    [MaxLength(512)] public string? MethodEn { get; set; }

    /// <summary>Tần suất nguyên văn spec gốc — tra cứu, KHÔNG điều khiển hành
    /// vi (quyết định D1: kiểm mọi lô).</summary>
    [MaxLength(256)] public string? SourceFrequency { get; set; }

    /// <summary>
    /// Hạng mục này dựng từ <b>ma trận tiêu chuẩn</b> vì mã nguyên liệu chưa có
    /// spec riêng — không phải tiêu chuẩn của chính mã đó.
    ///
    /// <para>590/946 mã trong MES chưa có spec, nên đây là đường chạy thường
    /// xuyên. Không có cờ này thì không ai phân biệt được hồ sơ kiểm theo spec
    /// thật với hồ sơ kiểm theo mặc định.</para>
    /// </summary>
    public bool FromDefaultMatrix { get; set; }

    /// <summary>
    /// Tiêu chuẩn là KHUÔN MẪU chưa điền (<c>"FTM: XXX"</c>). 521/5 961 dòng
    /// thư viện ở trạng thái này. UI hiện "chưa xác định — hỏi QA" và KHÔNG
    /// tính vào điều kiện đủ để kết luận lô: không bắt ai ký lên tiêu chí trống.
    /// </summary>
    public bool AcceptanceUnspecified { get; set; }
}
