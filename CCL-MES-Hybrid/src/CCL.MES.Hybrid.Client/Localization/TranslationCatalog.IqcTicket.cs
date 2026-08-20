namespace CCL.MES.Hybrid.Client.Localization;

// feat/iqc-ticket — nhãn cho header phiếu IQC (9 field từ VM thật) + form
// "Thêm phiếu". Mọi chuỗi hiển thị mới đi qua đây (đủ VI + EN parity) — KHÔNG
// hardcode trong .razor (skill cmes-i18n-parity).
public sealed partial class TranslationCatalog
{
    private void RegisterIqcTicket()
    {
        //     key                          vi                                      en
        // ── Header field labels (9) ───────────────────────────────────────
        Add("iqc.f.receipt",             "Phiếu nhập",                           "Receipt");
        Add("iqc.f.codeifs",             "Code IFS",                             "Code IFS");
        Add("iqc.f.matdesc",             "Mô tả vật liệu",                       "Material description");
        Add("iqc.f.ifsdesc",             "Mô tả IFS",                            "IFS description");
        Add("iqc.f.lotbatch",            "Số lô / batch",                        "Lot/Batch No");
        Add("iqc.f.manfdate",            "Ngày sản xuất",                        "Manf date");
        Add("iqc.f.maker",               "Nhà sản xuất",                         "Maker");
        Add("iqc.f.supplier",            "Nhà cung cấp",                         "Supplier");
        Add("iqc.f.inspector",           "Người kiểm",                           "Inspector");
        Add("iqc.f.qty",                 "Số lượng",                             "Quantity");
        Add("iqc.f.uom",                 "Đơn vị",                               "Unit");
        Add("iqc.f.samplesize",          "Cỡ mẫu",                               "Sample size");
        Add("iqc.f.expiry",              "Hạn dùng",                             "Expiry");
        Add("iqc.f.empty",               "—",                                     "—");

        // ── Add-ticket button + form ──────────────────────────────────────
        Add("iqc.add.button",            "＋ Thêm phiếu",                        "＋ Add ticket");
        Add("iqc.form.title",            "Tạo phiếu IQC",                        "Create IQC ticket");
        Add("iqc.form.desc",             "Số phiếu và người kiểm do hệ thống tự sinh. Nhập Code IFS để tự điền mô tả vật liệu.",
                                                                                 "Receipt number and inspector are auto-generated. Enter the Code IFS to auto-fill the material description.");
        Add("iqc.form.codeifs.ph",       "Nhập hoặc quét Code IFS",              "Enter or scan Code IFS");
        Add("iqc.form.lotbatch.ph",      "Số lô / batch",                        "Lot / batch number");
        Add("iqc.form.maker.ph",         "Chọn hoặc gõ nhà sản xuất",            "Pick or type a maker");
        Add("iqc.form.supplier.ph",      "Nhà cung cấp",                         "Supplier");
        Add("iqc.form.qty.ph",           "Số lượng nhận",                        "Received quantity");
        Add("iqc.form.uom.ph",           "vd. kg, cuộn, tờ",                     "e.g. kg, roll, sheet");
        Add("iqc.form.save",             "Lưu phiếu",                            "Save ticket");
        Add("iqc.form.cancel",           "Huỷ",                                  "Cancel");
        Add("iqc.form.saving",           "Đang lưu…",                            "Saving…");

        // ── Match-status warnings ─────────────────────────────────────────
        Add("iqc.match.matched",         "Đã khớp Code IFS trong danh mục.",     "Code IFS matched in the catalog.");
        Add("iqc.match.ambiguous",       "Code IFS trùng nhiều bản ghi — không tự điền mô tả. Vẫn có thể lưu tạm.",
                                                                                 "Code IFS matches multiple records — description not auto-filled. You can still save.");
        Add("iqc.match.unmatched",       "Không tìm thấy Code IFS trong danh mục — lưu tạm, sẽ đối chiếu sau.",
                                                                                 "Code IFS not found in the catalog — saved provisionally for later reconciliation.");

        // ── Result / errors ───────────────────────────────────────────────
        Add("iqc.result.created",        "Đã tạo phiếu",                         "Ticket created");
        Add("iqc.err.invalid_code_ifs",  "Code IFS bắt buộc (1–64 ký tự).",      "Code IFS is required (1-64 characters).");
        Add("iqc.err.invalid_lot_batch_no", "Số lô / batch bắt buộc (1–64 ký tự).", "Lot/Batch No is required (1-64 characters).");
        Add("iqc.err.invalid_quantity",  "Số lượng phải lớn hơn 0.",             "Quantity must be greater than zero.");
        Add("iqc.err.receipt_no_conflict", "Trùng số phiếu, thử lại.",           "Receipt number clash, please retry.");
        Add("iqc.err.lot_duplicate",     "Lô đã tồn tại cho vật liệu này.",      "That lot already exists for this material.");
        Add("iqc.err.generic",           "Không lưu được phiếu.",                "Could not save the ticket.");

        // ── Search-by-description + multi-select (feat/iqc-search-by-desc) ─
        Add("iqc.f.matdesc.search",      "Tìm theo mô tả vật liệu",              "Search by material description");
        Add("iqc.search.ph",             "Gõ ≥3 ký tự mô tả (vd. NITTO 5000NS)", "Type ≥3 chars of the description (e.g. NITTO 5000NS)");
        Add("iqc.search.tooshort",       "Gõ tối thiểu 3 ký tự để tìm.",         "Type at least 3 characters to search.");
        Add("iqc.search.results",        "{0}/{1} kết quả",                      "{0}/{1} results");
        Add("iqc.search.filter.ph",      "Lọc trong kết quả…",                   "Filter results…");
        Add("iqc.search.none",           "Không có Code IFS khớp mô tả này.",     "No Code IFS matches this description.");
        Add("iqc.multi.created",         "Đã tạo {0} phiếu.",                    "Created {0} tickets.");
        Add("iqc.multi.partial",         "Đã tạo {0} phiếu, {1} lỗi.",           "Created {0} tickets, {1} failed.");
        Add("iqc.multi.codeifs.pick",    "Chọn Code IFS (tick nhiều)",           "Pick Code IFS (multi-select)");
        Add("iqc.multi.lotbatch.hint",   "Nhiều Code IFS → tự thêm hậu tố -01…-0N vào số lô.",
                                                                                 "Multiple Code IFS → lot number auto-suffixed -01…-0N.");

        // ── Bảng dòng-vật-tư (feat/iqc-materials-line-table) — mỗi Code IFS đã
        //    tick = 1 dòng, Quantity + UoM riêng. UoM = chip Roll | Pcs. ──
        Add("iqc.lines.col.select",      "Chọn",                                 "Select");
        Add("iqc.lines.col.mother",      "Mã mẹ",                                "Mother code");
        Add("iqc.lines.col.partdesc",    "Mô tả part",                           "Part description");
        Add("iqc.lines.col.width",       "Khổ (mm)",                             "Width (mm)");
        Add("iqc.uom.roll",              "Cuộn",                                 "Roll");
        Add("iqc.uom.pcs",               "Cái",                                  "Pcs");
    }
}
