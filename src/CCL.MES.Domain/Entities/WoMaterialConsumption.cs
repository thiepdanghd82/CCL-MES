namespace CCL.MES.Domain.Entities;

/// <summary>
/// A1 — một LẦN QUÉT lô lên một dòng BOM của WO. Bảng append-only.
///
/// <para><b>Đ4 (Henry): mỗi lần quét sinh một dòng riêng.</b> Đặc tả gốc dựa
/// vào <c>UNIQUE(WoMaterialId, MaterialLotId) WHERE ReversedAt IS NULL</c> để
/// quét lặp không sinh dòng thứ hai. Đ4 <b>xoá bỏ index đó</b>. Ba hệ quả kéo
/// theo, đều đã thi công:</para>
/// <list type="number">
///   <item>Chống bấm nhầm chuyển hoàn toàn sang <c>Idempotency-Key</c>
///   (middleware đã có, đã test). Cùng key → 1 dòng; khác key → 2 dòng và
///   <b>cả hai đều hiện trong hồ sơ</b>. Đây là đánh đổi có ý thức: hồ sơ chi
///   tiết hơn, chống bấm nhầm yếu hơn — phải ghi rõ trong tài liệu vận hành
///   để operator không ngạc nhiên.</item>
///   <item>Backfill mất chỗ dựa idempotent ⇒ thay bằng dấu
///   <c>CreatedBy = "backfill-a1"</c> + điều kiện <c>NOT EXISTS</c>.</item>
///   <item>Con số "đã tiêu hao bao nhiêu" phải <c>SUM(QtyUsed)</c> theo
///   <c>(WoMaterialId, MaterialLotId)</c> — <b>không</b> lấy dòng cuối.</item>
/// </list>
///
/// <para><b>Đảo tiêu thụ = đánh dấu <see cref="ReversedAt"/>/<see cref="ReversedBy"/>/
/// <see cref="ReversedReason"/>. TUYỆT ĐỐI KHÔNG <c>DELETE</c>, KHÔNG
/// <c>UPDATE QtyUsed</c>.</b> Dòng sai vẫn phải nhìn thấy được — đó là điểm
/// khác nhau giữa sổ kế toán và sổ nháp.</para>
/// </summary>
public class WoMaterialConsumption : BaseEntity
{
    /// <summary>WO đã tiêu thụ lô. CASCADE theo WO.</summary>
    public long WoId { get; set; }

    /// <summary>Leg (nhánh routing) nếu WO đã fork. <b>Bắt buộc nullable</b> —
    /// hôm nay <c>WoLegs</c> có 0 dòng, mọi WO còn 1-leg. RESTRICT.</summary>
    public long? LegId { get; set; }

    /// <summary>Dòng BOM đã nhận lô. CASCADE theo dòng BOM.</summary>
    public long WoMaterialId { get; set; }

    /// <summary>Lô đã dùng. <b>RESTRICT</b> — không được xoá lô còn vết tiêu
    /// thụ, nếu không mạch truy xuất đứt giữa chừng.</summary>
    public long MaterialLotId { get; set; }

    public double QtyUsed { get; set; }
    public string? Uom { get; set; }

    public string ScannedBy { get; set; } = "";
    public DateTime ScannedAt { get; set; }

    public DateTime? ReversedAt { get; set; }
    public string? ReversedBy { get; set; }
    public string? ReversedReason { get; set; }
}
