using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P12 — thư viện tiêu chuẩn kiểm tra NVL đầu vào (IQC). BA bảng, tách bạch
/// theo đúng hình dạng dữ liệu thật.
///
/// <para><b>VÌ SAO KHÔNG DÙNG CHUNG <see cref="CheckItemLibrary"/>:</b> khoá
/// scope khác hẳn — IPQC/FQC/OQC scope theo QC line resolve từ routing, IQC
/// scope theo NGUYÊN LIỆU qua số spec. Và quan trọng hơn: ở IPQC một hạng mục
/// có MỘT tiêu chuẩn dùng chung, còn ở IQC <b>tiêu chuẩn khác nhau theo từng
/// nguyên liệu</b> — đo được trên dữ liệu thật: <c>BD-01</c> (độ bám dính) có
/// <b>60 tiêu chuẩn khác nhau</b> trên 451 nguyên liệu, <c>CU-01</c> có 11.
/// Nhồi hai mô hình vào một bảng là mời đúng loại nhầm lẫn mà
/// L61 đã trả giá.</para>
///
/// <para>Nguồn: <c>IQC_Master_Tieu_chuan_kiem_tra_NVL.xlsx</c> — tổng hợp
/// 19/08/2026 từ 809 file spec gốc. Scope proposal:
/// <c>docs/p12-iqc-library-scope-proposal.md</c>.</para>
/// </summary>
public class IqcCheckItemLibrary : BaseEntity
{
    /// <summary>Natural key — mã hạng mục chuẩn hoá: <c>NL-01</c> · <c>NQ-02</c>
    /// · <c>KT-01</c> … Unique index, dùng upsert idempotent.</summary>
    [MaxLength(16)] public string ItemId { get; set; } = "";

    /// <summary>Mã nhóm, lấy từ tiền tố của <see cref="ItemId"/>: NL · NQ · KT ·
    /// MT · BD · CU · XS · TL · BO · KH.</summary>
    [MaxLength(8)] public string GroupCode { get; set; } = "";

    /// <summary>Nhãn nhóm — là KHOÁ TAB trên UI. Giữ chuỗi VI làm định danh,
    /// chỉ <see cref="GroupLabelEn"/> mới dùng để hiển thị khi chọn EN; dịch
    /// khoá thì đổi ngôn ngữ sẽ văng tab (bài học L60).</summary>
    [MaxLength(64)] public string GroupLabelVi { get; set; } = "";
    [MaxLength(64)] public string? GroupLabelEn { get; set; }

    /// <summary>
    /// P13 — nhóm vật liệu áp dụng. <see cref="IqcMaterialCategory.Any"/> = áp
    /// cho mọi nhóm (tem nhãn, hồ sơ HSF). 21 hạng mục có sẵn từ trước đều
    /// backfill về <c>Any</c>: chúng vốn được dựng làm bộ CHUNG, đổi sang một
    /// nhóm cụ thể là viết lại lịch sử của phiếu đã đóng băng.
    /// </summary>
    /// <remarks>
    /// <b>Vì sao ENUM chứ không phải string + static-const như L27</b>
    /// (<c>IqcGroup</c>, <c>MaterialLot.Status</c>, <c>WoLeg.LegKind</c>).
    /// <para>Lợi ích L27 nêu là "thêm giá trị thứ 5 chỉ cần thêm const + seed,
    /// 0 migration". Lợi ích đó ở đây KHÔNG áp dụng: mỗi nhóm vật liệu kéo theo
    /// một bộ hạng mục riêng, một luật chấm riêng và một lưới nhập riêng —
    /// thêm nhóm thứ 5 là phải viết code dù cột có kiểu gì.</para>
    /// <para>Đổi lại, enum lưu-dạng-chuỗi được <c>gate-enum-integrity</c> quét
    /// TỰ ĐỘNG (scanner đi theo model EF tìm property kiểu enum có
    /// <c>HasConversion</c>). Khai là <c>string</c> thì gate lướt qua mà vẫn báo
    /// PASS — đã thử: 40 cột trước, 43 cột sau khi đổi sang enum.</para>
    /// </remarks>
    public IqcMaterialCategory Category { get; set; } = IqcMaterialCategory.Any;

    /// <summary>
    /// P13 — hạng mục được ghi nhận kiểu gì (đạt/không · đếm lỗi · đo lặp · hồ
    /// sơ). Quyết định ô nhập nào hiện ra và luật chấm nào chạy, nên nó thuộc
    /// THƯ VIỆN chứ không phải UI — hai màn hình khác nhau không được phép hiểu
    /// khác nhau về cùng một hạng mục.
    /// </summary>
    public IqcCheckKind Kind { get; set; } = IqcCheckKind.Verdict;

    /// <summary>
    /// P13 — số lần đo của hạng mục <see cref="IqcCheckKind.Measure"/>. File
    /// master dùng <b>5</b> cho mọi hạng mục kích thước, CỐ ĐỊNH bất kể cỡ lô —
    /// khác hẳn cỡ mẫu ngoại quan vốn tra theo lô. Hạng mục khác để 0.
    /// </summary>
    public int MeasureCount { get; set; }

    [MaxLength(256)] public string ItemVi { get; set; } = "";
    [MaxLength(256)] public string? ItemEn { get; set; }

    /// <summary>
    /// Hạng mục thuộc <b>MA TRẬN TIÊU CHUẨN</b> — bộ áp cho gần như mọi nguyên
    /// liệu, dùng khi mã chưa có spec riêng (quyết định D3, Henry 2026-08-28).
    ///
    /// <para>Dữ liệu tự chia đôi rất sắc, không có vùng xám: 13 hạng mục phủ
    /// 92–100% số spec, 8 hạng mục còn lại phủ 0–6%. Ngưỡng 92% đặt vào đúng
    /// khe đó.</para>
    ///
    /// <para><b>590/946 mã nguyên liệu trong MES chưa có spec</b> — đây là ĐA SỐ
    /// chứ không phải ngoại lệ, nên đường ma trận là đường chạy thường xuyên.</para>
    /// </summary>
    public bool InDefaultMatrix { get; set; }

    /// <summary>
    /// Tiêu chuẩn / phương pháp DỰ PHÒNG — giá trị phổ biến nhất trên toàn thư
    /// viện, chỉ dùng khi nguyên liệu chưa có spec riêng.
    ///
    /// <para><b>KHÔNG BAO GIỜ dùng cho nguyên liệu ĐÃ CÓ spec.</b> Với chúng,
    /// tiêu chuẩn phải lấy từ <see cref="IqcSpecItem"/> — gán giá trị chung ở
    /// đây cho mã có spec chính là cái bẫy §2.1 của scope proposal: sai mà vô
    /// hình, vì màn hình vẫn đầy chữ.</para>
    /// </summary>
    [MaxLength(1024)] public string? DefaultAcceptanceVi { get; set; }
    [MaxLength(1024)] public string? DefaultAcceptanceEn { get; set; }
    [MaxLength(512)] public string? DefaultMethodVi { get; set; }
    [MaxLength(512)] public string? DefaultMethodEn { get; set; }

    public int Sort { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// Nguyên liệu ↔ số spec ↔ nhà cung cấp. Một dòng cho mỗi số spec đã khử trùng.
/// </summary>
public class IqcMaterialSpec : BaseEntity
{
    /// <summary>Natural key — <c>CCL-SPEC-QCxxx</c>. Unique.</summary>
    [MaxLength(32)] public string SpecNo { get; set; } = "";

    /// <summary>Tên nguyên liệu, ĐÃ tách phần mã IFS trong ngoặc ra
    /// <see cref="MaterialCodeIfs"/>.</summary>
    [MaxLength(256)] public string MaterialCode { get; set; } = "";

    /// <summary>
    /// Mã <c>7xxxxxxx</c> trích từ phần trong ngoặc của tên nguyên liệu trong
    /// file spec — vd <c>TESA 4982(70000076)</c>. 46/459 spec có.
    ///
    /// <para><b>⚠ TÊN CỘT NÀY GÂY HIỂU NHẦM — ĐÂY KHÔNG PHẢI MÃ IFS CỦA MES.</b>
    /// Đo được 2026-08-28 trên live: <c>RawMaterials.PartNo</c> của MES có dạng
    /// <c>300xxxxx</c>, và phép nối <c>PartNo = MaterialCodeIfs</c> cho <b>0
    /// khớp</b> trên toàn bộ dữ liệu. Hai hệ đánh số khác hẳn nhau.</para>
    ///
    /// <para>Vậy <c>7xxxxxxx</c> là gì thì <b>chưa biết</b> — có thể là mã vật
    /// tư của NCC, hoặc mã hệ cũ. <b>KHÔNG dùng cột này để resolve</b> cho tới
    /// khi Ops xác nhận. Khoá nối thật là
    /// <c>RawMaterials.MotherCode = MaterialCode</c> (356/448 spec khớp).</para>
    ///
    /// <para>Giữ cột lại vì dữ liệu có thật và sẽ hữu ích khi biết nó là gì;
    /// đổi tên cột thì phải chờ biết tên đúng, đặt tên sai lần hai còn tệ hơn.</para>
    /// </summary>
    [MaxLength(32)] public string? MaterialCodeIfs { get; set; }

    [MaxLength(256)] public string? SupplierName { get; set; }

    /// <summary>Revision của bản spec được lấy (R01 · R03). Khi cùng một số spec
    /// có ở hai thư mục nguồn, bản cao hơn thắng.</summary>
    [MaxLength(16)] public string? Revision { get; set; }

    // ── P13: nguồn gốc + trạng thái duyệt ───────────────────────────────

    /// <summary>Phương pháp thử của tiêu chuẩn bám dính (<c>FTM1</c> 656× ·
    /// <c>FTM2</c> 421× · <c>ASTM D3330</c> 64× trong file master). Hai lô đo
    /// bằng hai phương pháp khác nhau thì con số KHÔNG so được với nhau, nên
    /// nó phải nằm cạnh tiêu chuẩn chứ không nằm trong ghi chú.</summary>
    [MaxLength(64)] public string? TestMethod { get; set; }

    /// <summary>Trạng thái duyệt. Spec import từ file ngoài vào ở
    /// <c>PendingQc</c>; phiếu dùng nó vẫn chạy nhưng UI hiện băng nhắc
    /// (Henry chốt 2026-09-04 — chặn thì QC sẽ quay lại Excel, mất luôn dấu
    /// vết).</summary>
    public IqcSpecApproval Approval { get; set; } = IqcSpecApproval.Approved;

    /// <summary>Nguồn nhập, để lần sau import biết dòng nào của mình
    /// (<c>iqc-report-2026</c>). <c>null</c> = tạo trong app.</summary>
    [MaxLength(64)] public string? ImportSource { get; set; }

    [MaxLength(128)] public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public bool Active { get; set; } = true;
}

/// <summary>
/// Tiêu chuẩn kiểm tra của MỘT hạng mục cho MỘT nguyên liệu — bảng mang giá trị
/// thật, 5 961 dòng.
///
/// <para>Đây là chỗ dễ làm sai nhất của cả tính năng: người dựng vội sẽ lấy
/// tiêu chuẩn từ <see cref="IqcCheckItemLibrary"/> (21 dòng, tiện hơn) và gán
/// một ngưỡng chung cho mọi vật liệu. Sai đó <b>vô hình</b> — màn hình vẫn đầy
/// chữ, chỉ là chữ sai, và người kiểm ký lên nó.</para>
/// </summary>
public class IqcSpecItem : BaseEntity
{
    /// <summary>Khoá tự nhiên BA thành phần:
    /// (<see cref="SpecNo"/>, <see cref="ItemId"/>, <see cref="Seq"/>).</summary>
    [MaxLength(32)] public string SpecNo { get; set; } = "";
    [MaxLength(16)] public string ItemId { get; set; } = "";

    /// <summary>
    /// Thứ tự tiêu chí TRONG cùng một (spec, hạng mục) — bắt đầu từ 1.
    ///
    /// <para><b>VÌ SAO KHOÁ PHẢI CÓ THÀNH PHẦN NÀY:</b> đợt chuẩn hoá gom 63
    /// biến thể về 21 mã hạng mục, nên một spec có thể có NHIỀU tiêu chí khác
    /// nhau cùng mang một mã. Đo được: 12 cặp như vậy, tối đa 3 tiêu chí.
    /// Ví dụ <c>CCL-SPEC-QC264 / NQ-06</c> (đóng gói mực đóng can) có 3 tiêu
    /// chí riêng — không rách/biến dạng · không ẩm ướt · nắp không rò rỉ.</para>
    ///
    /// <para>Khoá hai thành phần sẽ <b>âm thầm nuốt mất 13 tiêu chí kiểm</b>:
    /// import vẫn chạy, bảng vẫn đầy dòng, chỉ là người kiểm không còn được
    /// hỏi về nắp can. Đúng loại mất mát vô hình mà Nguyên tắc I của hiến
    /// pháp nói tới — test <c>IqcLibrarySeederTests</c> khoá con số 5 961.</para>
    /// </summary>
    public int Seq { get; set; } = 1;

    /// <summary>Tiêu chuẩn chấp nhận — RIÊNG cho nguyên liệu này.</summary>
    [MaxLength(1024)] public string? AcceptanceVi { get; set; }
    [MaxLength(1024)] public string? AcceptanceEn { get; set; }

    /// <summary>Phương pháp / thiết bị (cột "Ghi chú" của file gốc).</summary>
    [MaxLength(512)] public string? MethodVi { get; set; }
    [MaxLength(512)] public string? MethodEn { get; set; }

    /// <summary>
    /// Tần suất NGUYÊN VĂN trong spec gốc — "All lot" · "AQL GII 0.4" ·
    /// "Kiểm mỗi tháng một lần" · …
    ///
    /// <para><b>CHỈ ĐỂ TRA CỨU, KHÔNG ĐIỀU KHIỂN HÀNH VI.</b> Quyết định D1
    /// (Henry, 2026-08-28): kiểm TẤT CẢ hạng mục trên MỌI lô NVL về — chặt hơn
    /// spec gốc, nên không có rủi ro tuân thủ. Nhưng ghi đè chính sách KHÔNG
    /// được xoá dấu vết spec gốc nói gì: khi audit hỏi, phải trả lời được
    /// "spec ghi tháng, ta chủ động kiểm từng lô", chứ không phải "không biết
    /// spec ghi gì". 1 334/5 961 dòng ghi tần suất tháng.</para>
    /// </summary>
    [MaxLength(256)] public string? SourceFrequency { get; set; }

    // ── P13: ngưỡng SỐ đọc được từ AcceptanceVi, để máy tự chấm ──────────
    // AcceptanceVi vẫn là NGUỒN SỰ THẬT và vẫn hiện nguyên văn cho người kiểm —
    // mấy cột dưới chỉ là bản đọc được của nó. Low/Up/Nominal cùng null nghĩa
    // là "không có ngưỡng số" ⇒ người chấm, máy im lặng nhường quyền.

    /// <summary>Cận dưới. <c>null</c> = không chặn dưới.</summary>
    public double? LimitLow { get; set; }

    /// <summary>Cận trên. <c>null</c> = không chặn trên.</summary>
    public double? LimitUp { get; set; }

    /// <summary>Trị danh nghĩa nếu tiêu chuẩn có nêu (392 trong "392+0.4-0.2").</summary>
    public double? LimitNominal { get; set; }

    /// <summary>Đơn vị nguyên văn (<c>N/25mm</c>, <c>gf/in</c>, <c>mm</c>). Giữ
    /// lại vì nó là thứ phân biệt "270 N/m" với "270 mm".</summary>
    [MaxLength(32)] public string? LimitUnit { get; set; }

    /// <summary>Nhãn đứng trước trị trong tiêu chuẩn — <c>Face</c> · <c>Adhesive</c>
    /// · <c>Face+Adhesive</c>. Vứt nó đi là mất thông tin đang đo LỚP NÀO của
    /// cuộn: "Face 0.05±0.005" và "Adhesive 0.16±0.016" nằm trên cùng một mã và
    /// là hai phép đo khác nhau.</summary>
    [MaxLength(64)] public string? LimitLabel { get; set; }

    /// <summary>Tiêu chuẩn ghi "or tear" — vật liệu rách trước khi bong keo thì
    /// ĐẠT, vì lực bám đã lớn hơn độ bền của chính vật liệu. 226 lần xuất hiện
    /// trong file master; bỏ qua sẽ đánh trượt oan hàng đạt.</summary>
    public bool TearIsPass { get; set; }

    /// <summary>Bộ đọc có hiểu được <see cref="AcceptanceVi"/> không.
    /// <c>false</c> KHÔNG phải lỗi — 1% chuỗi thật sự mơ hồ. Nhưng phải ghi lại
    /// để đếm được, và để UI nói thẳng rằng máy không chấm hộ hạng mục này.</summary>
    public bool LimitParsed { get; set; }

    public int Sort { get; set; }
    public bool Active { get; set; } = true;
}

/// <summary>
/// P12 bước 4 — hồ sơ HSF của một MÃ NGUYÊN LIỆU (TDS · MSDS · RoHS · REACH ·
/// ISO 9001 · và loại do người dùng thêm).
///
/// <para><b>Gắn theo MÃ, không theo phiếu</b> (Henry chốt 2026-09-03). TDS là
/// thuộc tính của vật liệu, không phải của một lô: upload một lần thì mọi lô
/// sau của mã đó đều thấy. Gắn theo phiếu sẽ bắt người kiểm upload lại đúng
/// một file cho từng lô, và sáu tháng sau không ai biết bản nào là bản mới.</para>
///
/// <para>File KHÔNG nằm trong DB — chỉ giữ <see cref="StorageKey"/> trỏ vào
/// <c>IBlobStore</c> (<c>&lt;DataDir&gt;/blobs/IQC/Documents/&lt;mã&gt;/</c> trên
/// SERVER). DB nặng lên vì blob là cách chắc chắn để backup thành vô dụng.</para>
/// </summary>
public class IqcMaterialDocument : BaseEntity
{
    /// <summary>Khoá scope = <c>RawMaterials.MotherCode</c>, cùng khoá với
    /// <see cref="IqcMaterialSpec.MaterialCode"/>.</summary>
    [MaxLength(256)] public string MaterialCode { get; set; } = "";

    /// <summary>Mã loại hồ sơ, viết HOA không dấu: <c>TDS</c> · <c>MSDS</c> ·
    /// <c>ROHS</c> · <c>REACH</c> · <c>ISO9001</c>. Dùng đặt tên file nên phải
    /// an toàn với hệ thống tệp.</summary>
    [MaxLength(64)] public string DocType { get; set; } = "";

    [MaxLength(128)] public string? LabelVi { get; set; }
    [MaxLength(128)] public string? LabelEn { get; set; }

    // Ba trường BẮT BUỘC khi lưu — hồ sơ chất lượng không có số và hạn thì
    // không chứng minh được điều gì.
    [MaxLength(64)] public string? DocNumber { get; set; }
    public DateTime? IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }

    /// <summary>Khoá blob trên server; <c>null</c> = dòng đã khai nhưng chưa
    /// đính file.</summary>
    [MaxLength(512)] public string? StorageKey { get; set; }

    /// <summary>Tên file đã chuẩn hoá: <c>&lt;mã&gt;_&lt;DocType&gt;.pdf</c>.</summary>
    [MaxLength(256)] public string? FileName { get; set; }
    [MaxLength(64)] public string? FileSha256 { get; set; }
    public long? FileSizeBytes { get; set; }

    public int Sort { get; set; }

    /// <summary>Xoá MỀM. Hồ sơ chất lượng đã từng có mặt thì không được biến
    /// mất không dấu vết.</summary>
    public bool Active { get; set; } = true;
}
