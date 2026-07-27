namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/ImportSpecModal.razor (importspec.*).
public sealed partial class TranslationCatalog
{
    private void RegisterImportSpec()
    {
        //     key                                  vi                                                              en

        // Busy status (Uploading / Saving stages)
        Add("importspec.busy.uploading",         "Đang tải lên + đọc file…",                                      "Uploading + reading file…");
        Add("importspec.busy.saving",            "Đang lưu vào hệ thống…",                                        "Saving to the system…");

        // Banners
        Add("importspec.previewerror.title",     "Không đọc được file xlsx.",                                     "Could not read the xlsx file.");
        Add("importspec.saveerror.title",        "Lưu thất bại.",                                                 "Save failed.");
        Add("importspec.saveok.title",           "Đã lưu Spec mới.",                                              "New Spec saved.");
        Add("importspec.saveok.spec.prefix",     "Spec",                                                          "Spec");
        Add("importspec.saveok.rows",            "— {0} dòng dữ liệu.",                                           "— {0} data rows.");

        // Footer / action buttons
        Add("importspec.action.cancel",          "Hủy",                                                           "Cancel");
        Add("importspec.action.loading",         "Đang tải…",                                                     "Loading…");
        Add("importspec.action.choose.preview",  "Chọn file + xem trước",                                         "Choose file + preview");
        Add("importspec.action.processing",      "Đang xử lý…",                                                   "Processing…");
        Add("importspec.action.choose.another",  "Chọn file khác",                                                "Choose another file");
        Add("importspec.action.retry",           "Thử lại",                                                       "Retry");
        Add("importspec.action.saving",          "Đang lưu…",                                                     "Saving…");
        Add("importspec.action.saveascopy",      "Lưu thành bản sao mới",                                         "Save as copy");
        Add("importspec.action.supersede",       "Thay thế (Nâng Rev)",                                           "Supersede (Upgrade Rev)");
        Add("importspec.action.savetosystem",    "Lưu vào hệ thống",                                              "Save to system");

        // Stage titles (Modal header)
        Add("importspec.title.choose",           "Import Spec từ file xlsx",                                      "Import Spec from xlsx file");
        Add("importspec.title.uploading",        "Đang tải file…",                                                "Uploading file…");
        Add("importspec.title.previewok",        "Xem trước Spec",                                                "Spec preview");
        Add("importspec.title.previewerror",     "Lỗi đọc file",                                                  "File read error");
        Add("importspec.title.saving",           "Đang lưu…",                                                     "Saving…");
        Add("importspec.title.saveok",           "Hoàn tất",                                                      "Complete");
        Add("importspec.title.saveerror",        "Lỗi khi lưu",                                                   "Save error");
        Add("importspec.title.default",          "Import Spec",                                                   "Import Spec");

        // Choose stage — help text
        Add("importspec.help.prefix",            "Chọn loại công đoạn của Spec, sau đó nhấn",                     "Select the Spec's process type, then click");
        Add("importspec.help.choosefile",        "Chọn file",                                                     "Choose file");
        Add("importspec.help.suffix",            "để mở hộp thoại của hệ thống.",                                 "to open the system dialog.");
        Add("importspec.field.processtype",      "Loại công đoạn",                                                "Process type");
        Add("importspec.aria.plannercategory",   "Nhóm công đoạn",                                                "Planner category");
        Add("importspec.helpnote.prefix",        "File phải là",                                                  "The file must be a valid");
        Add("importspec.helpnote.suffix",        "hợp lệ, dung lượng ≤ 10 MB. Khi hộp thoại mở, hệ điều hành chỉ cho phép chọn file đúng định dạng.", "size ≤ 10 MB. When the dialog opens, the operating system will only allow files of the correct format.");

        // Preview meta
        Add("importspec.meta.file",              "File:",                                                         "File:");
        Add("importspec.meta.size",              "Dung lượng:",                                                   "Size:");
        Add("importspec.meta.process",           "Công đoạn:",                                                    "Process:");
        Add("importspec.warnings.summary",       "Cảnh báo khi đọc file ({0})",                                  "Warnings while reading the file ({0})");

        // Duplicate — Draft status (cannot replace)
        Add("importspec.dup.draft.title",        "Spec trùng đang ở trạng thái Nháp.",                           "Duplicate Spec is in Draft status.");
        Add("importspec.dup.draft.speccode",     "Mã Spec",                                                       "Spec code");
        Add("importspec.dup.draft.id",           "(id {0})",                                                      "(id {0})");
        Add("importspec.dup.draft.body",         "đã có REF NO này. Hệ thống không cho phép thay thế bản nháp — hãy sửa trực tiếp bản nháp đó hoặc đổi REF NO trong file rồi import lại.", "already has this REF NO. The system does not allow replacing a draft — edit that draft directly or change the REF NO in the file and import again.");

        // Duplicate — non-Draft status (choose action)
        Add("importspec.dup.exists.title",       "Đã tồn tại một Spec ở trạng thái {0} với cùng REF NO.",         "A Spec {0} with the same REF NO already exists.");
        Add("importspec.dup.exists.speccode",    "Mã Spec hiện có:",                                              "Existing Spec code:");
        Add("importspec.dup.exists.chooseaction","Chọn một hành động bên dưới.",                                 "Choose an action below.");
        Add("importspec.field.newspeccode",      "Mã Spec mới (nếu chọn \"Lưu thành bản sao mới\")",             "New Spec code (if \"Save as copy\")");
        Add("importspec.placeholder.copycode",   "vd. {0}-COPY",                                                  "e.g. {0}-COPY");
    }
}
