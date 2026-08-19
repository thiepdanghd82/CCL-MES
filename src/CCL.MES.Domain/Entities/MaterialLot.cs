namespace CCL.MES.Domain.Entities;

/// <summary>
/// A1 — trạng thái vòng đời của một lô nguyên vật liệu.
///
/// <para><b>Lưu dạng CHUỖI, không phải int</b> (bài học L27): thêm một trạng
/// thái về sau chỉ là dữ liệu, 0 migration; và đọc thẳng được trong DB khi điều
/// tra sự cố. Enum này chỉ là danh sách tên hợp lệ — cột
/// <see cref="MaterialLot.Status"/> giữ <c>ToString()</c> của nó.</para>
///
/// <code>
/// Quarantine ──IQC Pass──▶ Released ──QtyAvailable=0──▶ Consumed
///      │                      │  ▲                          │
///      │                      │  └──── đảo tiêu thụ ─────────┘  (Supervisor, Đ3)
///      └──IQC Fail──▶ Rejected│                                  QtyAvailable>0 ⇒ về Released
///         (terminal)          └──ExpiryAt&lt;now──▶ Expired
///                                                   │
///                                                   └─ QC kiểm lại + gia hạn ─▶ Released  (Đ3)
/// </code>
/// </summary>
public enum MaterialLotStatus
{
    /// <summary>Vừa nhập kho, chưa có kết luận IQC. KHÔNG được tiêu thụ.</summary>
    Quarantine,

    /// <summary>IQC Pass — được phép nạp lên máy.</summary>
    Released,

    /// <summary>IQC Fail — terminal, không có đường quay lại.</summary>
    Rejected,

    /// <summary>Đã dùng hết (<c>QtyAvailable</c> = 0). Đảo tiêu thụ đưa về
    /// <see cref="Released"/> khi số lượng trả lại &gt; 0 (Đ3).</summary>
    Consumed,

    /// <summary>Quá hạn ghi trên bao bì. QC kiểm lại + gia hạn ⇒ Released (Đ3),
    /// và đó là quyết định cần HAI vai khác nhau.</summary>
    Expired,
}

/// <summary>
/// A1 — một lô nguyên vật liệu vật lý (cuộn màng, thùng mực, kiện giấy…).
///
/// <para><b>Vì sao bảng này tồn tại.</b> Trước A1, <c>WoMaterials.LotNo</c> là
/// chuỗi tự do ghi thẳng từ ô nhập của operator. Đo trên live 2026-08-19: 5 dòng
/// có LotNo khác rỗng, <b>0</b> dòng khớp <c>RawMaterials.PartNo</c>, <b>0</b>
/// dòng khớp <c>IqcInspections.LotNumber</c>. Nghĩa là chuỗi lô hiện tại không
/// nối được với bất cứ thứ gì — khách hàng hỏi "cuộn màng nào đã vào đơn này,
/// phiếu IQC đâu" thì hệ không trả lời được. Truy xuất nguồn gốc phải đi bằng
/// KHOÁ SỐ; chuỗi chỉ được tồn tại như nhãn hiển thị.</para>
///
/// <para><b>Khoá tự nhiên chuỗi — ba lớp, đặt ở SCHEMA.</b>
/// (1) <c>COLLATE NOCASE</c> trên <see cref="LotNo"/> / <see cref="PartNo"/> /
/// <see cref="SupplierLotNo"/> — cấu hình ở <c>MesDbContext</c> bằng
/// <c>UseCollation("NOCASE")</c> trên CỘT, KHÔNG rải
/// <c>EF.Functions.Collate(...)</c> trong query (thừa, và phá cổng sang SQL
/// Server). (2) <c>CHECK (LotNo = TRIM(LotNo) AND LENGTH(LotNo) &gt; 0)</c> —
/// NOCASE không lo khoảng trắng. (3) <c>.Trim()</c> ở service. Lớp 3 là tiện
/// lợi; lớp 1+2 mới là bảo đảm. Bài học L28, áp dụng lần hai.</para>
///
/// <para><b>FK hybrid</b> mirror <see cref="IqcInspection"/>:
/// <see cref="RawMaterialId"/> nullable (resolve được thì set) +
/// <see cref="PartNo"/> snapshot bắt buộc (giữ lịch sử khi catalog đổi tên).
/// Vì <c>NULL ≠ NULL</c> trong unique index của SQLite, phải có CẢ HAI partial
/// unique index — xem cấu hình trong <c>MesDbContext</c>.</para>
///
/// <para><b>Đ2 (Henry):</b> <see cref="ExpiryAt"/> nhập tay khi làm IQC ⇒ A1
/// KHÔNG đụng bảng <c>RawMaterials</c> (2127 dòng).</para>
/// </summary>
public class MaterialLot : BaseEntity
{
    /// <summary>Mã lô in trên nhãn nhà cung cấp — khoá tự nhiên khi quét.
    /// <c>COLLATE NOCASE</c> + <c>CHECK</c> TRIM ở schema.</summary>
    public string LotNo { get; set; } = "";

    /// <summary>FK tới danh mục vật tư khi resolve được; null khi lô về kho mà
    /// catalog chưa có part đó.</summary>
    public long? RawMaterialId { get; set; }

    /// <summary>Snapshot mã part tại thời điểm nhập kho. Bắt buộc — đây là thứ
    /// duy nhất còn lại khi <see cref="RawMaterialId"/> null.</summary>
    public string PartNo { get; set; } = "";

    /// <summary>Phiếu IQC đã kết luận cho lô này. Tên thực thể giữ nguyên
    /// <c>IqcInspection</c> (Đ5) — <c>IqcReceipt</c> KHÔNG tồn tại.</summary>
    public long? IqcInspectionId { get; set; }

    public string? SupplierName { get; set; }

    /// <summary>Mã lô theo cách đánh số của nhà cung cấp (khác
    /// <see cref="LotNo"/> khi kho tự đánh lại). NOCASE.</summary>
    public string? SupplierLotNo { get; set; }

    public DateTime ReceivedAt { get; set; }

    /// <summary>Hạn dùng — Đ2: nhập tay lúc làm IQC.</summary>
    public DateTime? ExpiryAt { get; set; }

    public double QtyReceived { get; set; }

    /// <summary>Còn lại sau các lần tiêu thụ. Xuống 0 ⇒ <c>Consumed</c>.</summary>
    public double QtyAvailable { get; set; }

    public string? Uom { get; set; }

    /// <summary><see cref="MaterialLotStatus"/>.ToString().</summary>
    public string Status { get; set; } = nameof(MaterialLotStatus.Quarantine);

    public string? StatusReason { get; set; }
    public string? StatusChangedBy { get; set; }
    public DateTime? StatusChangedAt { get; set; }

    // ── Bốn cột vòng đời từ Đ3 ────────────────────────────────────
    // Gia hạn lô hết hạn là quyết định rủi ro CAO HƠN release lần đầu (vật tư
    // đã quá hạn ghi trên bao bì), nên nó cần HAI vai khác nhau: người kiểm
    // lại (Retested*) ≠ người duyệt gia hạn (ExpiryExtended*). Bốn cột này là
    // bằng chứng của luật đó, không phải metadata trang trí.

    /// <summary>Thời điểm QC kiểm lại lô đã hết hạn.</summary>
    public DateTime? RetestedAt { get; set; }

    /// <summary>Người kiểm lại — chữ ký 1. Phải KHÁC
    /// <see cref="ExpiryExtendedBy"/>.</summary>
    public string? RetestedBy { get; set; }

    /// <summary>Hạn mới sau khi gia hạn.</summary>
    public DateTime? ExpiryExtendedTo { get; set; }

    /// <summary>Người duyệt gia hạn — chữ ký 2.</summary>
    public string? ExpiryExtendedBy { get; set; }

    /// <summary>L38 Option-B: EF gửi <c>X''</c> lúc INSERT, trigger SQLite
    /// <c>randomblob(8)</c> bump; <c>IsConcurrencyToken</c> để hai operator
    /// quét cùng một lô không tiêu thụ vượt tồn.</summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
