namespace CCL.MES.Domain.Entities;

/// <summary>
/// Phương án C — Bước 6 (đóng finding #3 + #7, honor quyết định #5). Bảng DỮ LIỆU
/// map "tín hiệu routing → QC line", thay cho keyword/StartsWith hardcode trong
/// <c>QcLineResolver</c>. Sửa map = sửa SEED (idempotent), không sửa code.
///
/// <para>Một dòng = một luật khớp. <see cref="MatchType"/> ∈
/// {ProcessCode | WorkCenterPrefix | OpKeyword}; <see cref="QcLine"/> ∈
/// {LABEL | DIGITAL | SILK | PRESS_CNC | NONE}. NONE = nhận diện công đoạn
/// KHÔNG sinh item IPQC (pre-press, sấy, FQC/OQC…) — hợp lệ, khác Unmapped.</para>
///
/// <para>Ưu tiên resolve (trong resolver): ProcessCode khớp chính xác →
/// WorkCenterPrefix (khớp DÀI nhất) → OpKeyword (chứa, Sort nhỏ thắng) → Unmapped.</para>
/// </summary>
public class ProcessLineMap : BaseEntity
{
    /// <summary>Loại khớp: "ProcessCode" | "WorkCenterPrefix" | "OpKeyword".</summary>
    public string MatchType { get; set; } = "";

    /// <summary>Giá trị khớp: mã process / tiền tố WorkCenterNo / keyword (case-insensitive).</summary>
    public string MatchValue { get; set; } = "";

    /// <summary>QC line kết quả: LABEL · DIGITAL · SILK · PRESS_CNC · NONE.</summary>
    public string QcLine { get; set; } = "";

    public int Sort { get; set; }
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
}
