using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Domain.Entities;

/// <summary>
/// P10.7d-1 — single-row IPQC review per WO (1:1, enforced by unique
/// index on <see cref="WorkOrderId"/>). Mirrors the 7b
/// <c>WoPlateCheck</c> / <c>WoCutterCheck</c> shape but with 4 status
/// slots (Material + 3 print checks per SpecHub §3 line 130) + a
/// judgment trio + a QA approval trio.
///
/// Per Q1 (Henry-confirmed 2026-06-06), the 3 print-check slots are
/// hardcoded at the entity level — Color (ΔE default 2) / Registration
/// / Content (text+barcode). Per-product threshold override defers
/// to 7e.
///
/// Per Q2, single row not per-row table. Audit granularity per Q7 is
/// achieved at the controller layer (one <c>WO_IPQC_CHECK</c> audit
/// row per slot mutation), not by sharding the entity.
///
/// Per §5.1 overwriteable until QC submits a judgment; once
/// <see cref="Judgment"/> is non-Pending the controller rejects
/// further slot mutations with 422 invalid_phase.
/// </summary>
public class WoIpqcCheck : BaseEntity
{
    public long WorkOrderId { get; set; }

    // ── ① Material recheck (lot-level, NOT per-material) ─────────
    // SpecHub §3 line 129: "verify lại lần cuối, OK/NG cho cả lô".

    public IpqcCheckStatus MaterialStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? MaterialNgReasonCode { get; set; }
    [MaxLength(500)] public string? MaterialNgNote { get; set; }

    // ── ② Print check A — Color (default ΔE ≤ 2) ─────────────────
    // SpecHub §3 line 132. Per-product threshold defers to 7e.

    public IpqcCheckStatus PrintAStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? PrintANgReasonCode { get; set; }
    [MaxLength(500)] public string? PrintANgNote { get; set; }

    // ── ② Print check B — Registration ───────────────────────────
    // SpecHub §3 line 133: "Registration (chồng màu)".

    public IpqcCheckStatus PrintBStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? PrintBNgReasonCode { get; set; }
    [MaxLength(500)] public string? PrintBNgNote { get; set; }

    // ── ② Print check C — Content (text + barcode) ───────────────
    // SpecHub §3 line 134: "Content (text + barcode)".

    public IpqcCheckStatus PrintCStatus { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? PrintCNgReasonCode { get; set; }
    [MaxLength(500)] public string? PrintCNgNote { get; set; }

    // ── ③ Judgment (3-button pattern per SpecHub §3 lines 138-145) ─

    public IpqcJudgment Judgment { get; set; } = IpqcJudgment.Pending;

    /// <summary>Required when <see cref="Judgment"/> = SpecialAccept.
    /// SpecHub §3 line 144: "BẮT BUỘC nhập lý do".</summary>
    [MaxLength(500)] public string? SpecialAcceptReason { get; set; }

    /// <summary>QC username at judgment write time. Pinned to the
    /// auth claim; the dual-sig guard compares this against the
    /// QA approver's username (see §5.5.1 dual-sig contract).</summary>
    [MaxLength(128)] public string? IpqcSubmittedBy { get; set; }
    public DateTime? IpqcSubmittedAt { get; set; }

    // ── QA approval (only meaningful when Judgment = SpecialAccept) ─

    public QaOutcome QaOutcome { get; set; } = QaOutcome.Pending;

    /// <summary>Required when <see cref="QaOutcome"/> = Reject.
    /// Optional when Approve (QA can leave a note but it's not
    /// gated).</summary>
    [MaxLength(500)] public string? QaReason { get; set; }

    /// <summary>QA approver username. Per §5.5.1, MUST NOT equal
    /// <see cref="IpqcSubmittedBy"/> when feature flag
    /// `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER` is ON (default).
    /// Controller emits <c>WO_QA_APPROVE_DENIED</c> + 422
    /// <c>qa.same_user_as_ipqc_submitter</c> on violation.</summary>
    [MaxLength(128)] public string? QaApprovedBy { get; set; }
    public DateTime? QaApprovedAt { get; set; }

    // ── Concurrency (parent WO mediates per §6 + 7c-2 atomic pattern;
    //    UpdatedAt + UpdatedBy inherited from BaseEntity). Per 7b/7c
    //    convention this entity intentionally has NO RowVersion — the
    //    controller's WO row touch on every write bumps the parent's
    //    RowVersion via the existing SQLite UPDATE trigger so an
    //    optimistic-lock conflict at the entity level is impossible
    //    without the parent first conflicting. ───────────────────────

    // ── Phương án C — Bước 2: data-driven IPQC (SHADOW, additive) ────
    // Giữ NGUYÊN 4 slot cứng ở trên (legacy parity). Khi auto-sync
    // (Bước 4) materialize được bộ item từ CheckItemLibrary theo process
    // line resolve từ routing, các item rơi vào collection dưới đây và
    // <see cref="ItemsProfileSnapshotJson"/> đóng băng đúng-thời-điểm
    // (mirror WoQcCheck.ProfileSnapshotJson — sửa thư viện KHÔNG hồi tố).
    // Rollup ưu tiên Items khi có; rỗng → lùi về 4 slot cũ.

    /// <summary>Snapshot thư viện đóng băng lúc materialize (shape giống
    /// QcProfileSeed: {name, sections:[{id,title,items:[{key,label,spec,method,...}]}]}).
    /// NULL = WO cũ chưa materialize item → dùng 4 slot legacy.</summary>
    public string? ItemsProfileSnapshotJson { get; set; }

    /// <summary>QC line đã resolve cho WO này (vd "LABEL,PRESS_CNC") — audit/hiển thị.</summary>
    [MaxLength(128)] public string? ResolvedLines { get; set; }

    public ICollection<WoIpqcCheckItem> Items { get; set; } = new List<WoIpqcCheckItem>();
}

/// <summary>
/// Phương án C — Bước 2. Hạng mục IPQC data-driven (1:nhiều với
/// <see cref="WoIpqcCheck"/>), mirror <c>WoQcCheckItem</c>. Mỗi item bắt
/// nguồn từ <see cref="CheckItemLibrary"/> (qua resolver+auto-sync Bước 3/4)
/// nhưng được FREEZE vào row khi tạo — sửa thư viện sau đó không đổi item
/// đang chạy. Natural lookup = (WoIpqcCheckId, ItemKey).
/// </summary>
public class WoIpqcCheckItem : BaseEntity
{
    public long WoIpqcCheckId { get; set; }
    public WoIpqcCheck? WoIpqcCheck { get; set; }

    /// <summary>Khóa item — dùng <see cref="CheckItemLibrary.ItemId"/> (vd "LBL-A1");
    /// item legacy materialize từ 4 slot dùng key "material/print-a/print-b/print-c".</summary>
    [MaxLength(64)] public string ItemKey { get; set; } = "";

    /// <summary>QC line nguồn (LABEL/DIGITAL/SILK/PRESS_CNC) — cho scope mã lỗi (Bước 5).</summary>
    [MaxLength(16)] public string? ProcessLine { get; set; }

    [MaxLength(128)] public string? GroupLabel { get; set; }
    [MaxLength(512)] public string? Label { get; set; }
    [MaxLength(512)] public string? AcceptanceCriteria { get; set; }
    [MaxLength(256)] public string? Method { get; set; }

    // ── Bản EN — ĐÓNG BĂNG y như bản VI ngay bên trên ────────────────────
    // Bốn cột này KHÔNG phải bản dịch tra lúc hiển thị. Chúng được materializer
    // ghi một lần cùng lúc với bốn cột VI, nên hồ sơ đã ký đọc lại sau này ra
    // đúng chữ mà người vận hành đã thấy — ở CẢ hai ngôn ngữ. Sửa master data
    // về sau KHÔNG hồi tố, đúng như dòng VI.
    // NULL = hạng mục materialize trước khi có tính năng này, hoặc thư viện
    // thiếu bản dịch ⇒ UI rơi về bản VI, không bao giờ để trống ô.
    [MaxLength(128)] public string? GroupLabelEn { get; set; }
    [MaxLength(512)] public string? LabelEn { get; set; }
    [MaxLength(512)] public string? AcceptanceCriteriaEn { get; set; }
    [MaxLength(256)] public string? MethodEn { get; set; }
    [MaxLength(64)] public string? Severity { get; set; }
    /// <summary>Mã defect mặc định của hạng mục (gợi ý khi đánh NG) — từ thư viện.</summary>
    [MaxLength(64)] public string? DefectCode { get; set; }

    public IpqcCheckStatus Status { get; set; } = IpqcCheckStatus.Pending;
    [MaxLength(64)] public string? NgReasonCode { get; set; }
    [MaxLength(500)] public string? NgNote { get; set; }

    /// <summary>IPQC first-article (Henry 2026-08-25) — applicability toggle,
    /// default true (every materialised item starts checked). The IPQC leader
    /// unchecks items that don't apply to this product/run; a non-applicable
    /// item is EXCLUDED from the judgment readiness gate (mirrors the SETTING
    /// <c>WoSettingCheckItem.Applicable</c> pattern, L53).</summary>
    public bool Applicable { get; set; } = true;

    /// <summary>IPQC first-article (Henry 2026-08-25 Q3) — measured RESULT value
    /// entered on Dimension/Function items (e.g. "0.9", "83.5") or a qualitative
    /// note on Visual items (e.g. "Loang nhẹ"). Kept as free text so one column
    /// holds both numeric and descriptive results; NOT part of the readiness
    /// rollup (that stays on <see cref="Status"/>). Nullable — legacy items and
    /// items with no measurement leave it null.</summary>
    [MaxLength(128)] public string? MeasuredValue { get; set; }

    /// <summary>IPQC first-article (Q2) — check category frozen from
    /// <see cref="CheckItemLibrary.CheckType"/> at materialise
    /// (Visual / Measure→Dimension / Functional→Function). Drives the 3-tab
    /// stepper; snapshot so the grouping is stable even if the library changes.
    /// Nullable — legacy items materialised before this column stay null.</summary>
    [MaxLength(24)] public string? CheckType { get; set; }

    // ── P-IPQC-1 · đóng băng TIÊU CHÍ ĐÃ ÁP (Thiệp chốt 2026-09-11) ─────────
    // ISO 9001:2015 §8.6 đòi hồ sơ xuất xưởng mang bằng chứng phù hợp VỚI TIÊU
    // CHÍ CHẤP NHẬN. Trước đây thư viện có đủ Aql/Sampling ở 59/59 hạng mục
    // nhưng bộ dựng KHÔNG chép chúng xuống, và bảng này KHÔNG có cột chứa — nên
    // hồ sơ ghi được "Đạt" mà không ghi được ĐẠT THEO TIÊU CHÍ NÀO.

    /// <summary>Mức AQL đóng băng từ <see cref="CheckItemLibrary.Aql"/> lúc
    /// materialise (vd "0,65" · "1,5" · "4"). Giữ NGUYÊN CHUỖI của thư viện,
    /// không parse thành số: dấu thập phân là dấu phẩy theo vi-VN, và đây là
    /// bằng chứng chép lại chứ không phải giá trị để tính toán.</summary>
    [MaxLength(32)] public string? Aql { get; set; }

    /// <summary>Cách lấy mẫu đóng băng từ <see cref="CheckItemLibrary.Sampling"/>
    /// (vd "FAI 100% + AQL 1.5"). Nullable — hạng mục cũ materialise trước cột
    /// này để null.</summary>
    [MaxLength(128)] public string? Sampling { get; set; }

    // ── P-IPQC-2 · cỡ mẫu FAI theo CAVITY (Thiệp chốt 2026-09-11) ───────────
    // Lô của IPQC KHÔNG phải sản lượng lệnh và không liên quan IQC: nó là số
    // cavity của MỘT shot in hoặc MỘT shot cắt, và FAI là kiểm đủ 100% số ấy.
    // Lý do nghiệp vụ: khuôn/bản nhiều cavity sinh lỗi LẶP THEO VỊ TRÍ — cavity
    // số 7 hỏng thì mọi shot sau đều hỏng đúng con thứ 7. Lấy mẫu ngẫu nhiên
    // theo AQL trên cả lô rất dễ trượt lỗi ấy; soi đủ mọi cavity thì không.

    /// <summary>Số cavity phải kiểm ở first-article, giải lúc materialise theo
    /// công đoạn của chính hạng mục (in → <c>SpecPrints.Cavity</c>, cắt →
    /// <c>SpecFlexoCuttingRows.CuttingCavity</c>). <c>null</c> = CHƯA giải được
    /// — xem <see cref="CavitySource"/>. Không bao giờ mặc định 1: mặc định 1
    /// biến "kiểm đủ" thành "kiểm một con" mà không ai nhìn ra.</summary>
    public int? CavityCount { get; set; }

    /// <summary>Số cavity ở trên đến TỪ ĐÂU — <c>Print</c> · <c>Cut</c> ·
    /// <c>Manual</c> (người kiểm nhập tay) · <c>Ambiguous</c> (spec có nhiều
    /// giá trị cavity cắt khác nhau, đo được 59/238 spec) · <c>Missing</c>
    /// (spec chưa điền). Ghi lại nguồn để người đọc hồ sơ về sau phân biệt
    /// được "lấy từ spec" với "người kiểm tự điền".</summary>
    [MaxLength(16)] public string? CavitySource { get; set; }

    // ── Dung sai THEO SẢN PHẨM, đóng băng từ QcCriteria (2026-09-11) ────────
    // Thư viện là theo DÒNG SẢN XUẤT nên không thể mang dung sai: nhãn 20mm và
    // nhãn 200mm cùng hạng mục "kích thước tổng thể" nhưng khác dung sai. Con
    // số ấy sống ở QcCriteria (per ProductRevision × Stage), nối vào đây qua
    // QcCriterion.LibraryItemKey và ĐÓNG BĂNG lúc materialise — hồ sơ phải
    // giữ NGƯỠNG ĐÃ ÁP, không tra lại về sau khi kỹ sư đã sửa spec.

    /// <summary>Cận dưới đã áp. <c>null</c> = hạng mục này không có ngưỡng số
    /// (hoặc chưa ai soạn QC plan cho sản phẩm) ⇒ máy KHÔNG chấm, người chấm.</summary>
    public double? LimitLow { get; set; }

    /// <summary>Cận trên đã áp.</summary>
    public double? LimitUp { get; set; }

    /// <summary>Giá trị danh nghĩa nếu spec có ghi — để hiện "20 ± 0,5" cho
    /// người kiểm, không dùng để chấm.</summary>
    public double? LimitNominal { get; set; }

    /// <summary>Đơn vị của ba số trên (mm · µm · %…). Thiếu đơn vị thì con số
    /// vô nghĩa, nên đóng băng cùng chứ không suy lúc hiển thị.</summary>
    [MaxLength(16)] public string? LimitUnit { get; set; }

    /// <summary><c>SpecQcWindow.Id</c> đã cấp ngưỡng, để truy ngược hồ sơ về
    /// đúng bản kế hoạch QC nào. <c>null</c> = không có spec nào áp.</summary>
    public long? LimitSourceWindowId { get; set; }

    public int Sort { get; set; }
}
