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

    // ── P13 — CỠ MẪU: máy đề xuất, người quyết, và luôn thấy được cả hai ──

    /// <summary>Cỡ lô dùng để tra bảng lấy mẫu (số cuộn · số tấm · số can ·
    /// số cái). KHÁC <see cref="Quantity"/> vốn là lượng nhập theo đơn vị
    /// thương mại (m², mét dài, kg) — tra bảng AQL bằng số mét là ra cỡ mẫu vô
    /// nghĩa. <c>null</c> = phiếu cũ, chưa ai khai.</summary>
    public long? LotQty { get; set; }

    /// <summary>Cỡ mẫu máy ĐỀ XUẤT tại thời điểm mở phiếu, đóng băng lại.
    /// Không tính lại lúc đọc: bảng lấy mẫu có thể đổi, và khi đó phiếu cũ phải
    /// vẫn nói đúng điều đã xảy ra hôm đó.</summary>
    public int? SampleSizeSuggested { get; set; }

    /// <summary>Lý do QC đổi khác đề xuất. Henry chốt 2026-09-04: <b>mọi</b>
    /// thay đổi đều phải ghi lý do — kể cả khi lấy NHIỀU hơn (siết chặt), vì
    /// một hồ sơ chất lượng không được có con số nào không giải thích được.
    /// <c>null</c> khi QC giữ nguyên đề xuất.</summary>
    [MaxLength(512)] public string? SampleSizeOverrideReason { get; set; }

    /// <summary>
    /// P13 bước 4 — NHÓM vật liệu đã dùng để dựng bộ hạng mục cho phiếu này.
    ///
    /// <para>Suy ra một lần lúc mở phiếu (<see cref="Application.Services"/> ·
    /// <c>IqcCategoryRule</c>) rồi ĐÓNG BĂNG. Không tính lại mỗi lần đọc: đơn vị
    /// tồn kho của nguyên liệu có thể được sửa ở IFS sau khi phiếu đã ký, và lúc
    /// đó phiếu cũ sẽ tự đổi bộ hạng mục dưới chân người đã ký — đúng thứ
    /// Nguyên tắc IV cấm.</para>
    ///
    /// <para><c>Any</c> = không suy được (đơn vị lạ). Phiếu vẫn mở, người kiểm
    /// nhận bộ hạng mục dùng chung; nói KHÔNG BIẾT tốt hơn đoán bừa về Roll rồi
    /// bắt người ta bấm qua 13 ô đếm lỗi vô nghĩa.</para>
    /// </summary>
    public IqcMaterialCategory MaterialCategory { get; set; } = IqcMaterialCategory.Any;
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

    // ── P13 — ĐẾM LỖI + dấu vết máy chấm / người đổi ─────────────────────

    /// <summary>Số lỗi đếm được, cho hạng mục kiểu <c>DefectCount</c>.
    /// <c>null</c> = CHƯA ĐẾM — khác hẳn 0 (đã đếm, không có lỗi nào). Ép
    /// chưa-đếm về 0 là tuyên bố lô đạt thay cho người chưa làm việc (L67).</summary>
    public int? DefectCount { get; set; }

    /// <summary>Kết luận MÁY chấm: <c>Pass</c> · <c>Fail</c> · <c>Undecidable</c>.
    /// Đóng băng lại kể cả khi người đổi khác — auditor phải trả lời được "máy
    /// nói gì, ai đổi, vì sao".</summary>
    [MaxLength(16)] public string? AutoVerdict { get; set; }

    /// <summary>Mã lý do máy đưa ra kết luận đó (<c>iqc.judge.defect_found</c>,
    /// <c>iqc.judge.above_up</c>…). Mã chứ không phải câu đã dịch: câu dịch đổi
    /// theo ngôn ngữ và theo thời gian, mã thì không.</summary>
    [MaxLength(64)] public string? AutoVerdictReason { get; set; }

    /// <summary>Vị trí phép đo / ô đếm làm trượt (1-based). Không có nó thì
    /// người kiểm phải tự dò lại 5 con số để biết cái nào sai.</summary>
    public int? AutoVerdictOffendingSeq { get; set; }

    /// <summary>Lý do người đổi khác kết luận của máy. BẮT BUỘC khi
    /// <see cref="Pass"/> khác <see cref="AutoVerdict"/> (Henry chốt
    /// 2026-09-04: máy chấm là RÀNG BUỘC, đổi phải ghi lý do).</summary>
    [MaxLength(512)] public string? OverrideReason { get; set; }

    /// <summary>Ai đổi, lúc nào. Server đóng dấu theo token — client không khai
    /// được, vì đây là bằng chứng chứ không phải lời khai.</summary>
    [MaxLength(128)] public string? OverriddenBy { get; set; }
    public DateTime? OverriddenAt { get; set; }

    /// <summary>Vật liệu RÁCH trước khi bong keo — chỉ có nghĩa khi tiêu chuẩn
    /// ghi "or tear". Người kiểm tick, và nó biến một trị dưới ngưỡng thành
    /// ĐẠT, nên phải nằm trong hồ sơ chứ không chỉ trong đầu người kiểm.</summary>
    public bool TearObserved { get; set; }

    // ── P13 bước 4 — HÌNH DẠNG và NGƯỠNG đóng băng lúc mở phiếu ─────────
    // Cùng lý do với khối P12 ngay dưới: sửa thư viện / sửa spec về sau KHÔNG
    // được hồi tố hồ sơ đã ký. Ở đây hậu quả nặng hơn nhãn hiển thị: đổi
    // `Kind` của một hạng mục từ Verdict sang Measure sẽ làm phiếu cũ đổi
    // HÌNH DẠNG ô nhập, và đổi ngưỡng sẽ làm `AutoVerdict` đã đóng băng mâu
    // thuẫn với ngưỡng hiện hành mà không ai giải thích được.

    /// <summary>Ghi nhận kiểu gì: <c>Verdict</c> · <c>DefectCount</c> ·
    /// <c>Measure</c> · <c>Document</c>. Quyết định ô nhập nào hiện và luật
    /// chấm nào chạy. Bản ghi trước P13 là <c>Verdict</c> — đúng với hành vi cũ
    /// (người bấm đạt/không đạt), không phải một giá trị bịa cho đủ cột.</summary>
    public IqcCheckKind Kind { get; set; } = IqcCheckKind.Verdict;

    /// <summary>Số phép đo phải nhập, cho <see cref="IqcCheckKind.Measure"/>.
    /// 0 với mọi kiểu khác. Số dòng <c>IqcResultMeasurements</c> dựng sẵn bằng
    /// đúng con số này.</summary>
    public int MeasureCount { get; set; }

    /// <summary>Cận dưới / cận trên đã dùng để chấm. <c>null</c> = không có cận
    /// đó. Cả hai null ⇒ không có ngưỡng số ⇒ máy nhường người chấm.</summary>
    public double? LimitLow { get; set; }
    public double? LimitUp { get; set; }

    /// <summary>Đơn vị của ngưỡng (<c>mm</c>, <c>N/25mm</c>…). Để đọc hồ sơ mà
    /// không phải mở lại spec.</summary>
    [MaxLength(32)] public string? LimitUnit { get; set; }

    /// <summary>Nhãn phân biệt khi một hạng mục có nhiều ngưỡng —
    /// <c>Face</c> / <c>Adhesive</c> nói ngưỡng này đo lớp nào.</summary>
    [MaxLength(64)] public string? LimitLabel { get; set; }

    /// <summary>Tiêu chuẩn có ghi "or tear" không. Chỉ khi cờ này bật thì
    /// <see cref="TearObserved"/> mới biến một trị dưới cận thành ĐẠT.</summary>
    public bool TearIsPass { get; set; }

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
