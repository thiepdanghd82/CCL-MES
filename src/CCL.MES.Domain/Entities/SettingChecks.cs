using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P10.7g — per-item SETTING check, PERSISTED (thay attestation-cục-bộ 7c-3/#213).
/// Mirror <see cref="WoIpqcCheckItem"/>: mỗi hạng mục makeready (Print/Cut) một row,
/// FREEZE nhãn + tiêu chuẩn lúc materialize (sửa thư viện sau KHÔNG đổi WO đang chạy).
/// Concurrency đi qua WO.RowVersion (7c-2 pattern) — entity này không có RowVersion.
/// Natural lookup = (WorkOrderId, ProcessKind, ItemKey).
/// </summary>
public class WoSettingCheckItem : BaseEntity
{
    public long WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    /// <summary>"Print" | "Cut" — quy trình (sub-tab).</summary>
    [MaxLength(8)] public string ProcessKind { get; set; } = "";

    /// <summary>Khóa hạng mục = <see cref="CheckItemLibrary.ItemId"/> (vd "SET-PR-00");
    /// hàng ad-hoc (F4) dùng key "adhoc-{n}".</summary>
    [MaxLength(64)] public string ItemKey { get; set; } = "";

    // ── Snapshot đóng băng lúc materialize ──────────────────────────────
    [MaxLength(512)] public string? Label { get; set; }
    [MaxLength(512)] public string? Standard { get; set; }
    [MaxLength(128)] public string? GroupLabel { get; set; }

    /// <summary>F1 — hạng mục có áp dụng cho WO này không (mặc định true).
    /// false = N/A → loại khỏi advance-guard, không ghi nhận OK/NG.</summary>
    public bool Applicable { get; set; } = true;

    public PrepressCheckStatus Status { get; set; } = PrepressCheckStatus.Pending;

    /// <summary>F3 — mã defect đã chọn khi NG (<see cref="CheckItemDefectOption.DefectCode"/>).</summary>
    [MaxLength(64)] public string? DefectCode { get; set; }
    [MaxLength(500)] public string? NgNote { get; set; }

    /// <summary>F4 — hàng thêm ad-hoc tại hiện trường (không từ master); chỉ sống per-WO.</summary>
    public bool AdHoc { get; set; }

    [MaxLength(128)] public string? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }

    public int Sort { get; set; }
}

/// <summary>
/// P10.7g — defect option per hạng mục (drop-list khi NG). Base = seed
/// (<see cref="ProductCode"/> null, dùng chung mọi mã). Người dùng "＋ Thêm mới"
/// (QC-add-new) → row <see cref="ProductCode"/>=&lt;mã SP&gt; nhớ cho LOT sau.
/// Natural lookup = (ItemId, DefectCode, ProductCode).
/// </summary>
public class CheckItemDefectOption : BaseEntity
{
    /// <summary>Khóa hạng mục = <see cref="CheckItemLibrary.ItemId"/>.</summary>
    [MaxLength(64)] public string ItemId { get; set; } = "";

    [MaxLength(64)] public string DefectCode { get; set; } = "";
    [MaxLength(256)] public string LabelVi { get; set; } = "";
    [MaxLength(256)] public string LabelEn { get; set; } = "";

    /// <summary>NULL = base (mọi mã SP dùng chung). Có giá trị = defect user thêm
    /// cho riêng mã SP đó (per-product, nhớ LOT sau).</summary>
    [MaxLength(64)] public string? ProductCode { get; set; }

    public bool Active { get; set; } = true;
    public int Sort { get; set; }
}
