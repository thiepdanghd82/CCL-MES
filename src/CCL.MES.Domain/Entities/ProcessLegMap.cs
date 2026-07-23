namespace CCL.MES.Domain.Entities;

/// <summary>
/// P11-1 — bảng DỮ LIỆU map "tín hiệu routing → leg" (mirror
/// <see cref="ProcessLineMap"/>). Một dòng = một luật khớp op → suy ra
/// (<see cref="LegKind"/>, <see cref="Method"/>, <see cref="ProcessLine"/>).
/// Sửa routing behaviour = sửa SEED (idempotent), KHÔNG sửa code —
/// đúng triết lý Plan C.
///
/// <para><see cref="MatchType"/> ∈ {ProcessCode | WorkCenterPrefix |
/// OpKeyword}; ưu tiên resolve giống <c>QcLineResolver.Classify</c>:
/// ProcessCode chính xác → WorkCenterPrefix (dài nhất) → OpKeyword
/// (chứa, Sort nhỏ thắng) → Unmapped.</para>
///
/// <para>Ghép với <see cref="ProcessLineMap"/> (đã có) ở chỗ:
/// ProcessLineMap trả QC line cho IPQC-item; ProcessLegMap trả THÊM
/// LegKind + Method để dựng leg DAG. Hai bảng cố ý tách để không phá
/// hành vi Plan C hiện tại.</para>
/// </summary>
public class ProcessLegMap : BaseEntity
{
    /// <summary>"ProcessCode" | "WorkCenterPrefix" | "OpKeyword".</summary>
    public string MatchType { get; set; } = "";

    /// <summary>Mã process / tiền tố WorkCenterNo / keyword (case-insensitive).</summary>
    public string MatchValue { get; set; } = "";

    /// <summary><c>LegKind</c> kết quả: PRINT|CUT|TAPE|ASSEMBLY|PRINT_CUT.</summary>
    public string LegKind { get; set; } = "";

    /// <summary>Phương pháp/máy hiển thị: Silkscreen|HP|Flexo|RDC|CNC…</summary>
    public string Method { get; set; } = "";

    /// <summary>QC line token Plan C: SILK|DIGITAL|LABEL|PRESS_CNC|FINISHING.</summary>
    public string ProcessLine { get; set; } = "";

    public int Sort { get; set; }
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
}
