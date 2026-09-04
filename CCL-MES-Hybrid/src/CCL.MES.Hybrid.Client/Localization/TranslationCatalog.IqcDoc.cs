namespace CCL.MES.Hybrid.Client.Localization;

// P12 bước 4 — bảng hồ sơ HSF theo MÃ MẸ nguyên liệu (mục 1 của stepper phiếu
// IQC): TDS · MSDS · RoHS · REACH · ISO 9001. Đủ VI + EN parity (skill
// cmes-i18n-parity) — KHÔNG hardcode chuỗi trong .razor.
public sealed partial class TranslationCatalog
{
    private void RegisterIqcDoc()
    {
        //     key                          vi                                          en
        // ── trạng thái bảng ───────────────────────────────────────────────
        Add("iqc.doc.nomaterial",      "Chọn một nguyên liệu ở ô tìm theo mô tả để xem hồ sơ HSF của mã đó.",
                                                                                   "Pick a material in the description search to see its HSF documents.");
        Add("iqc.doc.loading",         "Đang tải hồ sơ…",                          "Loading documents…");
        Add("iqc.doc.empty",           "Mã này chưa có dòng hồ sơ nào.",           "No document rows for this code yet.");
        Add("iqc.doc.folder",          "Thư mục trên server: IQC / Documents / {0}",
                                                                                   "Server folder: IQC / Documents / {0}");

        // ── ba trường bắt buộc ────────────────────────────────────────────
        Add("iqc.doc.required",        "Cần đủ số hiệu · ngày cấp · hạn (hạn phải sau ngày cấp).",
                                                                                   "Number, issue date and expiry are all required (expiry after issue).");
        Add("iqc.doc.status.missing",  "Thiếu dữ liệu",                            "Incomplete");

        // ── lưu ───────────────────────────────────────────────────────────
        Add("iqc.doc.save",            "Lưu {0} thay đổi",                         "Save {0} change(s)");
        Add("iqc.doc.save.blocked",    "Chưa lưu được: còn dòng thiếu dữ liệu bắt buộc.",
                                                                                   "Cannot save yet: a changed row is missing required data.");
        Add("iqc.doc.saved",           "Đã lưu {0} dòng.",                         "Saved {0} row(s).");

        // ── thêm / xoá dòng ───────────────────────────────────────────────
        Add("iqc.doc.add.type.ph",     "Mã loại, vd ISO14001",                     "Type code, e.g. ISO14001");
        Add("iqc.doc.add.label.ph",    "Tên hiển thị (không bắt buộc)",            "Display name (optional)");
        Add("iqc.doc.add.save",        "Thêm",                                     "Add");
        Add("iqc.doc.add.cancel",      "Huỷ",                                      "Cancel");
        Add("iqc.doc.removed",         "Đã gỡ dòng {0}. File trên server vẫn còn.",
                                                                                   "Removed row {0}. The file stays on the server.");

        // ── file ──────────────────────────────────────────────────────────
        Add("iqc.doc.nofile",          "Chưa có file",                             "No file");
        Add("iqc.doc.nofile.hint",     "Dòng này chưa đính file — bấm chuột phải để nhập PDF.",
                                                                                   "No file attached — right-click to import a PDF.");
        Add("iqc.doc.open.hint",       "Nháy đúp để mở file",                      "Double-click to open the file");
        Add("iqc.doc.uploaded",        "Đã tải lên và đổi tên thành {0}.",         "Uploaded and renamed to {0}.");
        Add("iqc.doc.saved.only",      "Đã lưu file tại {0} nhưng máy không mở được.",
                                                                                   "File saved at {0} but the system could not open it.");

        // ── menu dòng (L35) ───────────────────────────────────────────────
        Add("iqc.doc.menu.aria",       "Thao tác trên dòng {0}",                   "Actions for row {0}");
        Add("iqc.doc.menu.open",       "Mở file",                                  "Open file");
        Add("iqc.doc.menu.import",     "Nhập file PDF…",                           "Import PDF…");
        Add("iqc.doc.menu.remove",     "Gỡ dòng này",                              "Remove this row");
        Add("iqc.doc.menu.external",   "Mở bằng app ngoài (Acrobat)",              "Open in external app (Acrobat)");

        // ── cửa sổ xem PDF trong app ──────────────────────────────────────
        Add("iqc.doc.viewer.aria",     "Cửa sổ xem tài liệu {0}",                  "Document viewer window {0}");
        Add("iqc.doc.viewer.controls", "Điều khiển xem tài liệu",                  "Document viewer controls");
        Add("iqc.doc.viewer.loading",  "Đang mở tài liệu…",                        "Opening document…");
        Add("iqc.doc.viewer.rendering","Đang dựng trang…",                         "Rendering pages…");
        Add("iqc.doc.viewer.zoomin",   "Phóng to",                                 "Zoom in");
        Add("iqc.doc.viewer.zoomout",  "Thu nhỏ",                                  "Zoom out");
        Add("iqc.doc.viewer.rotate",   "Xoay 90°",                                 "Rotate 90°");
        Add("iqc.doc.viewer.reset",    "Về mặc định",                              "Reset view");
        Add("iqc.doc.viewer.external", "Mở bằng app ngoài (Acrobat)",              "Open in external app (Acrobat)");
        Add("iqc.doc.viewer.save",     "Lưu về máy…",                              "Save to my computer…");
        Add("iqc.doc.viewer.saved",    "Đã lưu về {0}",                            "Saved to {0}");
        Add("iqc.doc.viewer.nohandler","Máy này chưa có app nào mở PDF — dùng bản xem trong app, hoặc bấm \"Lưu về máy…\".",
                                                                                   "No app on this machine opens PDFs — use the in-app preview, or click \"Save to my computer…\".");
        Add("iqc.doc.viewer.maxwins",  "Đang mở tối đa {0} cửa sổ. Đóng bớt rồi mở lại.",
                                                                                   "Maximum {0} windows are open. Close one and try again.");
        Add("iqc.doc.viewer.missing",  "Không tìm thấy bản đã tải về. Thử mở lại.",
                                                                                   "The downloaded copy is missing. Try opening it again.");
        Add("iqc.doc.viewer.failed",   "Không dựng được bản xem trước. Dùng \"Mở bằng app ngoài\".",
                                                                                   "Could not build the preview. Use \"Open in external app\".");
        Add("iqc.doc.viewer.toobig",   "File {0} vượt ngưỡng xem trong app ({1}) — dùng \"Mở bằng app ngoài\".",
                                                                                   "File is {0}, above the in-app preview limit ({1}) — use \"Open in external app\".");

        // ── lỗi từ server ─────────────────────────────────────────────────
        Add("iqc.doc.err.connect",     "Không kết nối được máy chủ: {0}",          "Cannot reach the server: {0}");
        Add("iqc.doc.err.timeout",     "Quá thời gian chờ. Kiểm tra mạng rồi thử lại.",
                                                                                   "Request timed out. Check the network and try again.");
        Add("iqc.doc.err.forbidden",   "Bạn không có quyền sửa hồ sơ HSF (cần QC trở lên).",
                                                                                   "You are not allowed to edit HSF documents (QC or above).");
        Add("iqc.doc.err.notfound",    "Dòng hồ sơ không còn tồn tại. Tải lại bảng.",
                                                                                   "This document row no longer exists. Reload the table.");
        Add("iqc.doc.err.required",    "Thiếu số hiệu, ngày cấp hoặc hạn.",        "Number, issue date or expiry is missing.");
        Add("iqc.doc.err.expiry",      "Hạn phải sau ngày cấp.",                   "Expiry must be after the issue date.");
        Add("iqc.doc.err.duplicate",   "Loại hồ sơ này đã có trong bảng.",         "This document type is already in the table.");
        Add("iqc.doc.err.toolarge",    "File vượt quá dung lượng cho phép.",       "File exceeds the allowed size.");
        Add("iqc.doc.err.emptyfile",   "File rỗng hoặc chưa chọn file.",           "File is empty or no file was chosen.");
        Add("iqc.doc.err.badtype",     "Mã loại hồ sơ không hợp lệ.",              "Document type code is not valid.");
        Add("iqc.doc.err.nomaterial",  "Thiếu mã nguyên liệu.",                    "Material code is missing.");
    }
}
