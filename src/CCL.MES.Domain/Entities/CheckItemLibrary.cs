namespace CCL.MES.Domain.Entities;

/// <summary>
/// Thư viện hạng mục kiểm (check-item library) dùng chung cho IPQC/FQC/OQC.
/// RE-MODEL v5 (sheet <c>IPQC_FQC_OQC_MAP</c> của <c>IPQC_Library_CMES_v5.xlsx</c>,
/// 59 item × 33 cột) theo MA TRẬN TICK-BOX: mỗi hạng mục mang 16 cờ bool cho
/// biết áp dụng với method/process nào (13 cột C~O) và stage nào (P/Q/R =
/// IPQC/FQC/OQC). Bind grid trực tiếp — mỗi cờ = 1 ô tick.
///
/// <para>Scope theo <see cref="ProcessLine"/> (v5: <c>LABEL</c> · <c>SILK</c>) và
/// (tùy chọn) mã hàng <see cref="ProductCode"/>. Resolver đọc routing → process
/// line → subset; auto-sync materialize subset (lọc <see cref="Ipqc"/>) vào check
/// + đóng băng snapshot. MASTER DATA — sửa ở đây KHÔNG hồi tố check đang chạy.</para>
///
/// <para>Natural key = <see cref="ItemId"/> (vd "LBL-A1" / "SLK-A1") — unique
/// index, dùng seed idempotent (upsert theo ItemId). Mỗi <see cref="DefectCode"/>
/// nhân bản sang <c>ReasonCode</c> (Kind=Scrap) khi import.</para>
/// </summary>
public class CheckItemLibrary : BaseEntity
{
    /// <summary>Natural key — mã hạng mục ổn định, vd "LBL-A1", "SLK-B3".</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Dòng SP / QC line (col B). v5: LABEL · SILK.</summary>
    public string ProcessLine { get; set; } = "";

    /// <summary>Scope theo mã hàng. NULL = áp cho mọi SP của process line (mặc định).</summary>
    public string? ProductCode { get; set; }

    /// <summary>Nhóm hạng mục (col S), vd "A·Ngoại quan".</summary>
    public string GroupLabel { get; set; } = "";
    /// <summary>Mã hạng mục trong nhóm (col T), vd "A1".</summary>
    public string Code { get; set; } = "";

    // ── Ma trận tick-box (v5 cột C~R). ● = true, · / rỗng = false ────────
    // 13 method/process (C~O)
    public bool BlankLabel { get; set; }   // C
    public bool Flexo { get; set; }        // D
    public bool LetterPress { get; set; }  // E
    public bool HpIndigo { get; set; }     // F
    public bool SilkScreen { get; set; }   // G
    public bool Flatbed { get; set; }      // H
    public bool Rdc { get; set; }          // I
    public bool Laminate { get; set; }     // J
    public bool Zebra { get; set; }        // K
    public bool SheetCut { get; set; }     // L
    public bool PunchHole { get; set; }    // M
    public bool DrillHole { get; set; }    // N
    public bool Slit { get; set; }         // O
    // 3 stage (P/Q/R) — thay QcStage cũ
    public bool Ipqc { get; set; }         // P
    public bool Fqc { get; set; }          // Q
    public bool Oqc { get; set; }          // R
    public bool Setting { get; set; }      // R+1 — P10.7g: hạng mục khâu SETTING (makeready)

    // ── Mô tả / tiêu chuẩn (v5 cột U~AG) ─────────────────────────────────
    public string ItemVi { get; set; } = "";
    public string ItemEn { get; set; } = "";
    public string AcceptanceVi { get; set; } = "";
    public string AcceptanceEn { get; set; } = "";

    /// <summary>Phương pháp · dụng cụ kiểm (col Y).</summary>
    public string? Method { get; set; }
    /// <summary>Mức nghiêm trọng, giữ nguyên chuỗi gốc, vd "◆ Critical" (col Z).</summary>
    public string? Severity { get; set; }
    public string? Aql { get; set; }
    public string? Sampling { get; set; }
    /// <summary>Loại kiểm tra, vd "Visual", "Measure", "Functional" (col AC).</summary>
    public string? CheckType { get; set; }

    /// <summary>Mã lỗi (defect) — nhân bản sang ReasonCode khi import (col AD).</summary>
    public string? DefectCode { get; set; }

    public string? IsoRef { get; set; }
    /// <summary>Điều kiện áp dụng / Condition (col AF).</summary>
    public string? AppliesWhen { get; set; }
    public string? Note { get; set; }

    public bool Active { get; set; } = true;
    public int Sort { get; set; }
}
