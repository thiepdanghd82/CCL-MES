using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P13 bước 5 — công đoạn PHÁT HIỆN ra lô hỏng.
///
/// <para>Đo trên sheet <c>NG Material</c> của file master 2026 (169 dòng có
/// dữ liệu): <c>IQC</c> 70 · <c>SX</c> 64 · để trống 34. Chỉ hai giá trị thật,
/// và <b>38% lô hỏng bị phát hiện ở SẢN XUẤT chứ không phải ở IQC</b> — nghĩa
/// là khối NG không được đóng kín trong màn IQC, nếu không thì 64 dòng một năm
/// sẽ tiếp tục sống ngoài app.</para>
/// </summary>
public enum IqcNgStage
{
    /// <summary>Không ghi rõ. 34/169 dòng lịch sử ở trạng thái này — nạp lên
    /// phải giữ nguyên "không biết" chứ không gán bừa về IQC.</summary>
    Unknown = 0,
    /// <summary>Phát hiện lúc kiểm nhập.</summary>
    Iqc = 1,
    /// <summary>Phát hiện khi đã đưa vào sản xuất.</summary>
    Production = 2,
}

/// <summary>
/// P13 bước 5 — vòng đời một vụ NG với nhà cung cấp.
///
/// <para>Năm trạng thái này ĐO ĐƯỢC từ 169 dòng thật, không phải bịa:</para>
/// <list type="bullet">
///   <item><c>Open</c> — 23 dòng chưa có ngày claim</item>
///   <item><c>Claimed</c> — 8 dòng "IQC đã TT (ngày) — NCC chưa trả lời"</item>
///   <item><c>SupplierConfirmed</c> — 6 dòng "Đã xác nhận chờ bù"</item>
///   <item><c>Settled</c> — 123 dòng (bù hàng 84 · giảm trừ tiền 39)</item>
///   <item><c>ClosedNoClaim</c> — 2 dòng "Close - k claim được NCC"</item>
/// </list>
/// </summary>
public enum IqcNgStatus
{
    /// <summary>Đã ghi nhận, chưa gửi claim.</summary>
    Open = 0,
    /// <summary>Đã báo NCC, chưa có hồi đáp.</summary>
    Claimed = 1,
    /// <summary>NCC đã xác nhận, đang chờ xử lý.</summary>
    SupplierConfirmed = 2,
    /// <summary>NCC đã xử lý xong (bù hàng / giảm trừ / trả hàng).</summary>
    Settled = 3,
    /// <summary>Khép lại mà không đòi được — vẫn phải giữ trong hồ sơ, vì đây
    /// chính là con số cần khi đàm phán lại hợp đồng với NCC đó.</summary>
    ClosedNoClaim = 4,
}

/// <summary>
/// P13 bước 5 — NCC đền bù bằng hình thức nào.
///
/// <para>Đo trên 169 dòng: bù hàng <b>84</b> · giảm trừ tiền <b>39</b> ·
/// trả hàng <b>3</b> · huỷ <b>0</b>. Thực tế chỉ hai hình thức được dùng thật;
/// hai hình thức còn lại giữ lại vì file master có cột riêng cho chúng
/// (<c>Ngày trả hàng</c>, <c>Ngày hủy</c>) nên nghiệp vụ có tính tới, chỉ là
/// năm nay chưa dùng.</para>
/// </summary>
public enum IqcClaimSettlement
{
    /// <summary>Chưa xử lý xong.</summary>
    None = 0,
    /// <summary>Bù hàng — hình thức phổ biến nhất (84/169).</summary>
    Replacement = 1,
    /// <summary>Giảm trừ tiền: trừ công nợ · cấn trừ · hoá đơn giảm trừ (39/169).</summary>
    CreditNote = 2,
    /// <summary>Trả hàng về NCC (3/169).</summary>
    Return = 3,
    /// <summary>Huỷ tại chỗ (0/169 năm 2026).</summary>
    Scrap = 4,
}

/// <summary>
/// P13 bước 5 — MỘT vụ nguyên liệu hỏng và việc đòi bồi thường nhà cung cấp.
///
/// <para><b>Vì sao mọi liên kết đều tuỳ chọn.</b> Đo trên sheet NG thật:</para>
/// <list type="bullet">
///   <item>38% vụ phát hiện ở SẢN XUẤT, khi không có phiếu IQC nào đang mở;</item>
///   <item>số lô trên sheet là số lô của NHÀ CUNG CẤP (<c>QT2502006</c>,
///     <c>VN-5689285-1</c>), khớp <c>MaterialLots.LotNo</c> 0/140 — app mới có
///     28 lô nên mẫu chưa đủ kết luận là khác hệ đánh số, nhưng đủ để KHÔNG
///     được tự động nối. Nối phải do người dùng chọn.</item>
/// </list>
///
/// <para><b>Khoá nối vật liệu là <see cref="PartNo"/>, KHÔNG phải mã mẹ.</b>
/// Đo được: mã trên sheet NG khớp <c>RawMaterials.PartNo</c> <b>122/146 =
/// 84%</b> và khớp <c>MotherCode</c> <b>0/146</b> — NGƯỢC HẲN với sheet tiêu
/// chuẩn, nơi mã mẹ là khoá và PartNo khớp 0. Hai sheet dùng hai hệ định danh
/// khác nhau cho cùng một vật liệu; dùng nhầm cái nào cũng ra 0 dòng.</para>
/// </summary>
public class IqcNgRecord : BaseEntity
{
    // ── nối vào dữ liệu app: TẤT CẢ đều tuỳ chọn ────────────────────────

    /// <summary>Phiếu IQC phát hiện ra, nếu có. <c>null</c> với vụ phát hiện ở
    /// sản xuất và với toàn bộ dữ liệu lịch sử.</summary>
    public long? IqcInspectionId { get; set; }

    /// <summary>Lô nguyên liệu, nếu người dùng chọn được. KHÔNG tự nối theo số
    /// lô — xem chú thích ở đầu lớp.</summary>
    public long? MaterialLotId { get; set; }

    /// <summary>Mã IFS của nguyên liệu — khoá nối ĐO ĐƯỢC (84%).</summary>
    [MaxLength(32)] public string? PartNo { get; set; }

    /// <summary>Số lô của NHÀ CUNG CẤP, nguyên văn. Đây là thứ dùng khi làm
    /// việc với NCC, nên phải giữ kể cả khi không nối được lô nội bộ.</summary>
    [MaxLength(64)] public string? SupplierLotNo { get; set; }

    [MaxLength(200)] public string? SupplierName { get; set; }
    [MaxLength(300)] public string? MaterialName { get; set; }

    /// <summary>Số P/O — cần khi đối chiếu công nợ với NCC.</summary>
    [MaxLength(64)] public string? PoNo { get; set; }

    // ── vụ việc ─────────────────────────────────────────────────────────

    public DateTime DetectedAt { get; set; }
    public IqcNgStage DetectedStage { get; set; } = IqcNgStage.Unknown;

    /// <summary>Tên lỗi nguyên văn ("Xước", "Khác màu", "K bám mực"). 59 chuỗi
    /// phân biệt trên 140 dòng — nhiều biến thể của cùng một lỗi ("Xước" 14 và
    /// "NG Xước" 8), nên chuẩn hoá về thư viện là việc RIÊNG, không làm lén ở
    /// đây bằng cách ép người nhập chọn từ danh sách chưa có.</summary>
    [MaxLength(256)] public string? DefectName { get; set; }

    /// <summary>Mã lỗi trong <c>ReasonCodes</c>, khi đã chuẩn hoá được.</summary>
    [MaxLength(32)] public string? DefectCode { get; set; }

    // ── số lượng: BA đơn vị cùng lúc, không phải một ─────────────────────
    // Đo trên 169 dòng: M² 162 · pcs/m 142 · số cuộn 126. Sheet ghi cả ba
    // song song vì mỗi bên dùng một đơn vị: kho đếm cuộn, NCC tính m², sản
    // xuất tính mét. Ép về một đơn vị là làm mất số của hai bên kia.

    public double? NgQty { get; set; }
    [MaxLength(16)] public string? NgUom { get; set; }
    public double? NgAreaM2 { get; set; }
    public int? NgRolls { get; set; }

    // ── claim ───────────────────────────────────────────────────────────

    public IqcNgStatus Status { get; set; } = IqcNgStatus.Open;

    /// <summary>Ngày gửi claim. <c>null</c> ⇒ chưa claim (23/169 dòng lịch sử).</summary>
    public DateTime? ClaimedAt { get; set; }

    /// <summary>Số tham chiếu claim nguyên văn — "CCL COMPLAINT 20260407",
    /// "CCL#260203 8D", "TT Nhóm Zalo". 95 chuỗi phân biệt: đây là SỐ HỒ SƠ tự
    /// do, không phải một danh mục để chuẩn hoá.</summary>
    [MaxLength(128)] public string? ClaimRef { get; set; }

    public IqcClaimSettlement Settlement { get; set; } = IqcClaimSettlement.None;
    public DateTime? SettledAt { get; set; }

    /// <summary>Trả lời của NCC, nguyên văn.</summary>
    [MaxLength(512)] public string? SupplierNote { get; set; }

    /// <summary>Ghi chú nội bộ.</summary>
    [MaxLength(512)] public string? Remark { get; set; }

    /// <summary>Dòng nạp từ file master lịch sử, không do người dùng nhập trong
    /// app. Không có cờ này thì không ai phân biệt được số liệu app tự sinh với
    /// số liệu chép từ Excel, và mọi báo cáo đối chiếu sau này đều mù.</summary>
    [MaxLength(64)] public string? ImportSource { get; set; }
}
