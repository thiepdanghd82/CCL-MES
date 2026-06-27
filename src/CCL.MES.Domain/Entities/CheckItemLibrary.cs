namespace CCL.MES.Domain.Entities;

/// <summary>
/// Phương án C — Bước 1. Thư viện hạng mục kiểm (check-item library) dùng chung cho
/// IPQC/FQC/OQC, scope theo <see cref="ProcessLine"/> (LABEL · DIGITAL · SILK · PRESS_CNC)
/// và (tùy chọn) theo mã hàng <see cref="ProductCode"/>. Nguồn dữ liệu:
/// <c>IPQC_Library_CMES_v2.csv</c> (101 item). Resolver (Bước 3) đọc routing → process line
/// → lấy subset bảng này; auto-sync (Bước 4) materialize subset vào check + đóng băng
/// <c>ProfileSnapshotJson</c>. Đây là MASTER DATA — sửa ở đây KHÔNG hồi tố check đang chạy.
///
/// <para>Natural key = <see cref="ItemId"/> (vd "LBL-A1") — unique index, dùng cho seed
/// idempotent (upsert theo ItemId). Mỗi <see cref="DefectCode"/> được nhân bản sang
/// <c>ReasonCode</c> (Kind=Scrap) khi import (quyết định #4: mở rộng ReasonCode, không
/// tạo bảng DefectCode mới).</para>
/// </summary>
public class CheckItemLibrary : BaseEntity
{
    /// <summary>Natural key — mã hạng mục ổn định, vd "LBL-A1", "DGT-B3".</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Dòng SP / QC line: LABEL · DIGITAL · SILK · PRESS_CNC (khớp ProcessCatalog map).</summary>
    public string ProcessLine { get; set; } = "";

    /// <summary>Scope theo mã hàng. NULL = áp cho mọi SP của process line (mặc định).</summary>
    public string? ProductCode { get; set; }

    /// <summary>Giai đoạn QC áp dụng: IPQC (mặc định cho thư viện v2) · FQC · OQC.</summary>
    public string QcStage { get; set; } = "IPQC";

    /// <summary>Nhóm hạng mục, vd "A·Ngoại quan".</summary>
    public string GroupLabel { get; set; } = "";
    /// <summary>Mã hạng mục trong nhóm, vd "A1".</summary>
    public string Code { get; set; } = "";

    public string ItemVi { get; set; } = "";
    public string ItemEn { get; set; } = "";
    public string AcceptanceVi { get; set; } = "";
    public string AcceptanceEn { get; set; } = "";

    /// <summary>Phương pháp · dụng cụ kiểm.</summary>
    public string? Method { get; set; }
    /// <summary>Mức nghiêm trọng (giữ nguyên chuỗi gốc, vd "◆ Critical").</summary>
    public string? Severity { get; set; }
    public string? Aql { get; set; }
    public string? Sampling { get; set; }
    /// <summary>Loại kiểm tra, vd "Visual", "Measure".</summary>
    public string? CheckType { get; set; }

    /// <summary>Mã lỗi (defect) — nhân bản sang ReasonCode khi import, vd "CONTENT".</summary>
    public string? DefectCode { get; set; }

    public string? ParetoPct { get; set; }
    /// <summary>Có thuộc form rút gọn (Y/N).</summary>
    public string? ShortForm { get; set; }
    public string? IsoRef { get; set; }
    /// <summary>Điều kiện áp dụng (vd chỉ khi có seri/barcode).</summary>
    public string? AppliesWhen { get; set; }
    public string? Note { get; set; }

    public bool Active { get; set; } = true;
    public int Sort { get; set; }
}
