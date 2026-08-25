namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — IpqcDashboard.razor (ipqc.*).
public sealed partial class TranslationCatalog
{
    private void RegisterIpqc()
    {
        //     key                              vi                                                                  en

        // ── Load / phase-guard states ─────────────────────────────────
        Add("ipqc.loading",                  "Đang tải trạng thái IPQC…",                                          "Loading IPQC status…");
        Add("ipqc.initial.error",            "Không tải được trạng thái IPQC:",                                    "Could not load IPQC status:");
        Add("ipqc.invalidphase.title",       "Lệnh SX không ở công đoạn IPQC_WAIT",                                "WO is not in the IPQC_WAIT phase");
        Add("ipqc.invalidphase.current",     "Hiện tại:",                                                          "Current:");
        Add("ipqc.invalidphase.hint",        "Quay lại tab Lệnh SX để chọn lệnh khác.",                            "Go back to the Work Orders tab to select another WO.");

        // ── Header counter pill ───────────────────────────────────────
        Add("ipqc.counter.items",            "Hạng mục đã xác nhận",                                               "Items confirmed");
        Add("ipqc.counter.slots",            "Slot đã xác nhận",                                                   "Slots confirmed");

        // ── Set-error banner ──────────────────────────────────────────
        Add("ipqc.seterror.title",           "Không lưu được thay đổi:",                                           "Could not save change:");
        Add("ipqc.dismiss",                  "Bỏ qua",                                                             "Dismiss");

        // ── Auto-sync (Phương án C) warnings ──────────────────────────
        Add("ipqc.autosync.title",           "Auto-sync chưa nạp bộ hạng mục:",                                  "Auto-sync did not load the item set:");
        Add("ipqc.autosync.skipped.unmapped","Routing có công đoạn chưa khai báo trong bảng map process→line. Báo kỹ thuật bổ sung map; tạm dùng 4-slot thủ công.",
                                             "The routing has an operation not declared in the process→line map. Ask engineering to add the mapping; use the manual 4-slot form for now.");
        Add("ipqc.autosync.skipped.nolibrary","Đã suy ra QC line nhưng thư viện hạng mục trống cho line đó. Báo QC nạp thư viện.",
                                             "The QC line was resolved but its item library is empty. Ask QC to load the library.");

        // ── Resolved-lines banner (data-driven items) ─────────────────
        Add("ipqc.resolved.label",           "QC line tự nạp theo routing:",                                       "QC line auto-loaded from routing:");
        Add("ipqc.resolved.count",           "{0} hạng mục",                                                       "{0} items");

        // ── Slot cards (legacy 4-slot fallback) — titles + subtitles ──
        Add("ipqc.slot.material.title",      "Kiểm lại vật liệu",                                                  "Material recheck");
        Add("ipqc.slot.material.subtitle",   "Đúng cuộn, lô, mã; đã vệ sinh sau khi canh máy",                     "Correct roll, lot, code, cleaned after setting");
        Add("ipqc.slot.print-a.title",       "In A — Màu (ΔE)",                                                    "Print A — Color (ΔE)");
        Add("ipqc.slot.print-a.subtitle",    "Đo ΔE ≤ ngưỡng spec (mặc định ≤ 2)",                                 "Measure ΔE ≤ spec threshold (default ≤ 2)");
        Add("ipqc.slot.print-b.title",       "In B — Chồng màu",                                                   "Print B — Registration");
        Add("ipqc.slot.print-b.subtitle",    "Chồng màu / canh lề trong dung sai",                                 "Color overlay / alignment within tolerance");
        Add("ipqc.slot.print-c.title",       "In C — Nội dung",                                                    "Print C — Content");
        Add("ipqc.slot.print-c.subtitle",    "Nội dung chữ + mã vạch khớp spec",                                   "Text content + barcode match spec");

        // ── Status pill display ───────────────────────────────────────
        Add("ipqc.status.ok",                "Đạt",                                                              "OK");
        Add("ipqc.status.ng",                "Lỗi",                                                              "NG");
        Add("ipqc.status.pending",           "— Chờ",                                                              "— Pending");

        // ── OK / NG buttons + NG form ─────────────────────────────────
        Add("ipqc.btn.ok",                   "Đạt",                                                                "OK");
        Add("ipqc.btn.ng",                   "Lỗi",                                                                "NG");
        Add("ipqc.btn.cancel",               "Huỷ",                                                                "Cancel");
        Add("ipqc.btn.savng",                "Lưu lỗi NG",                                                         "Save NG");
        Add("ipqc.ng.code.label",            "Mã lỗi NG:",                                                         "NG code:");
        Add("ipqc.ng.code",                  "Mã lỗi NG",                                                          "NG code");
        Add("ipqc.ng.select",                "— chọn mã lỗi NG —",                                                 "— select NG code —");
        Add("ipqc.ng.note.short",            "Ghi chú NG (1-500)",                                                 "NG note (1-500)");
        Add("ipqc.ng.note.long",             "Ghi chú NG (1-500 ký tự)",                                           "NG note (1-500 characters)");
        Add("ipqc.ng.catalog.empty",         "Danh mục NG trống — báo IT nạp mã lỗi (kind=Scrap).",                "NG catalog is empty — report to IT to seed reason codes (kind=Scrap).");

        // ── Judgment row ──────────────────────────────────────────────
        Add("ipqc.judgment.title",           "Kết luận IPQC",                                                      "IPQC judgment");
        Add("ipqc.judgment.gorun",           "Cho chạy",                                                           "Go Run");
        Add("ipqc.judgment.stopline",        "Dừng chuyền",                                                        "Stop Line");
        Add("ipqc.judgment.specialaccept",   "Chấp nhận đặc biệt",                                                 "Special Accept");
        Add("ipqc.rollup.allok",             "Cả 4 slot đều Đạt — nên chọn Cho chạy",                              "All 4 slots OK — Go Run recommended");
        Add("ipqc.rollup.anyng",             "Có slot bị NG — chọn Dừng chuyền hoặc Chấp nhận đặc biệt",           "One or more slots NG — choose Stop Line or Special Accept");
        Add("ipqc.rollup.pending",           "còn {0} slot chưa xác nhận",                                         "{0} slot(s) not yet confirmed");
        Add("ipqc.sa.reason.label",          "Lý do Chấp nhận đặc biệt (bắt buộc khi chọn Chấp nhận đặc biệt; 1-500 ký tự)",
                                             "Special Accept reason (required when choosing Special Accept; 1-500 characters)");

        // ── Judgment hints ────────────────────────────────────────────
        Add("ipqc.hint.notready",            "Xác nhận cả 4 slot (Vật liệu + In A/B/C) → các nút kết luận sẽ mở.",
                                             "Confirm all 4 slots (Material + Print A/B/C) → the judgment buttons become active.");
        Add("ipqc.hint.allok",               "Cả 4 slot đều Đạt — chỉ Cho chạy là hợp lệ. Dừng chuyền + Chấp nhận đặc biệt chỉ dùng khi có slot NG.",
                                             "All 4 slots OK — only Go Run is valid. Stop Line + Special Accept apply only when a slot is NG.");
        Add("ipqc.hint.anyng",               "Có slot bị NG — Cho chạy sẽ bị máy chủ từ chối. Chọn Dừng chuyền (về PREPRESS) hoặc Chấp nhận đặc biệt (chờ QA duyệt).",
                                             "One or more slots NG — Go Run is rejected by the server. Choose Stop Line (back to PREPRESS) or Special Accept (await QA approval).");

        // ── Non-localiser inline errors ───────────────────────────────
        Add("ipqc.error.no_code",            "Máy chủ trả về Ok=false nhưng không có mã lỗi — báo IT.",            "Server returned Ok=false without an error code — report to IT.");
        Add("ipqc.error.connect",            "Không kết nối được máy chủ: {0}",                                    "Could not connect to the server: {0}");
        Add("ipqc.error.loading",            "Lỗi khi tải trạng thái IPQC: {0}",                                   "Error loading IPQC status: {0}");

        // ── IPQC first-article — MATERIAL (SYSTEM) panel (h-3) ─────────
        Add("ipqc.material.title",           "Vật tư (theo hệ thống)",                                             "MATERIAL (SYSTEM)");
        Add("ipqc.material.subtitle",        "Đối chiếu LOT thực tế tại máy với dữ liệu gốc IQC",                   "Reconcile the actual lot at the machine against the source IQC data");
        Add("ipqc.material.loading",         "Đang tải đối chiếu vật tư…",                                          "Loading material reconciliation…");
        Add("ipqc.material.error",           "Không tải được đối chiếu vật tư:",                                    "Could not load material reconciliation:");
        Add("ipqc.material.empty",           "Chưa có dòng vật tư để đối chiếu.",                                   "No material lines to reconcile yet.");
        Add("ipqc.material.divergent.banner","Vật tư lệch dữ liệu gốc — kiểm tra trước khi tiếp tục.",              "Material diverges from the source data — verify before continuing.");
        Add("ipqc.material.col.system",      "VẬT TƯ (HỆ THỐNG)",                                                   "MATERIAL (SYSTEM)");
        Add("ipqc.material.col.code",        "MÃ VẬT TƯ",                                                           "MATERIAL CODE");
        Add("ipqc.material.col.iqclot",      "LOT IQC GỐC",                                                         "SOURCE IQC LOT");
        Add("ipqc.material.col.actual",      "THỰC TẾ TẠI MÁY",                                                     "ACTUAL AT MACHINE");
        Add("ipqc.material.col.confirm",     "XÁC NHẬN",                                                            "CONFIRM");
        Add("ipqc.material.divergent.tag",   "LỆCH",                                                               "DIVERGENT");
        Add("ipqc.material.none",            "—",                                                                  "—");

        // ── Engineer divergence waiver ────────────────────────────────
        Add("ipqc.material.waiver.pending",  "Chờ kỹ sư phê duyệt",                                                "Awaiting Engineer approval");
        Add("ipqc.material.waiver.approve",  "Kỹ sư phê duyệt",                                                     "Engineer approve");
        Add("ipqc.material.waiver.reject",   "Từ chối",                                                            "Reject");
        Add("ipqc.material.waiver.reason",   "Lý do phê duyệt / từ chối (1-500 ký tự)",                            "Approval / rejection reason (1-500 characters)");
        Add("ipqc.material.waiver.approvedby","Kỹ sư:",                                                            "Engineer:");
        Add("ipqc.material.waiver.approved", "Đã phê duyệt",                                                       "Approved");
        Add("ipqc.material.waiver.rejected", "Đã từ chối",                                                         "Rejected");
        Add("ipqc.material.waiver.reasonlabel","Lý do:",                                                           "Reason:");
        Add("ipqc.material.waiver.norole",   "Chờ kỹ sư phê duyệt (bạn không có quyền phê duyệt).",                 "Awaiting Engineer approval (you do not have approval rights).");

        // ── Process axis (Tier 1: Print / Cut / Other) ────────────────
        Add("ipqc.process.print",            "IN",                                                                 "PRINT");
        Add("ipqc.process.cut",              "CẮT",                                                                "CUT");
        Add("ipqc.process.other",            "Khác",                                                               "Other");
        // Badge "{confirmed}/{total}" beside the process chip label.
        Add("ipqc.process.badge",            "{0}/{1}",                                                            "{0}/{1}");

        // ── Item table column headers (per process → V/D/F) ───────────
        Add("ipqc.col.num",                  "#",                                                                  "#");
        Add("ipqc.col.applicable",           "Áp dụng",                                                            "Apply");
        Add("ipqc.applicable.hint",          "Bỏ chọn để loại hạng mục này khỏi đánh giá (không tính OK/NG).",     "Uncheck to exclude this item from judgment (no OK/NG required).");
        Add("ipqc.col.item",                 "Hạng mục",                                                           "Item");
        Add("ipqc.col.process",              "Công đoạn",                                                          "Process");
        Add("ipqc.col.method",               "Phương pháp",                                                        "Method");
        Add("ipqc.col.spec",                 "Tiêu chí",                                                           "Spec");
        Add("ipqc.col.result",               "Kết quả đo",                                                         "Result");
        Add("ipqc.col.verdict",              "Đánh giá",                                                          "Verdict");

        // ── First-article stepper tabs (Visual / Dimension / Function) ─
        Add("ipqc.tab.visual",               "Ngoại quan",                                                         "Visual");
        Add("ipqc.tab.dimension",            "Kích thước",                                                         "Dimension");
        Add("ipqc.tab.function",             "Chức năng",                                                          "Function");
        Add("ipqc.tab.other",                "Khác",                                                               "Other");
        Add("ipqc.tab.count",                "{0}",                                                                "{0}");

        // ── Measured value input (Dimension / Function items) ─────────
        Add("ipqc.item.measured.label",      "Giá trị đo",                                                         "Measured value");
        Add("ipqc.item.measured.placeholder","vd 0.9 / 83.5 / Loang nhẹ",                                          "e.g. 0.9 / 83.5 / slight bleed");
        Add("ipqc.item.measured.save",       "Lưu giá trị đo",                                                     "Save measured value");
        // Short label for the compact save button (wide table) so it never wraps.
        Add("ipqc.item.measured.save.short", "Lưu",                                                                "Save");
        // Short field label for the measured input inside the narrow-screen card.
        Add("ipqc.item.measured.short",      "Kết quả",                                                            "Result");
    }
}
