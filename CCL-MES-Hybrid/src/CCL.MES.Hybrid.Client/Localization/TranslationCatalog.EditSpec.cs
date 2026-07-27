namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/EditSpecModal.razor (editspec.*).
public sealed partial class TranslationCatalog
{
    private void RegisterEditSpec()
    {
        //     key                                     vi                                          en
        Add("editspec.header",                      "Sửa Spec {0}",                             "Edit Spec {0}");
        Add("editspec.no_data",                     "Chưa có dữ liệu Spec.",                    "No Spec data yet.");
        Add("editspec.field.title",                 "Tiêu đề",                                  "Title");
        Add("editspec.field.refno",                 "Số tham chiếu (REF NO)",                   "REF NO");
        Add("editspec.field.inspection_level",      "Spec / Mức kiểm tra",                      "Spec / Inspection level");
        Add("editspec.inspection_level.placeholder", "ví dụ A166",                              "e.g. A166");
        Add("editspec.field.process_code",          "Mã công đoạn",                             "Process code");
        Add("editspec.field.color_json",            "ColorSpec JSON",                           "ColorSpec JSON");
        Add("editspec.color_json.placeholder",      "Mảng JSON các màu, để trống = giữ nguyên", "JSON array of colors, blank = keep unchanged");
        Add("editspec.cancel",                      "Hủy",                                      "Cancel");
        Add("editspec.saving",                      "Đang lưu…",                                "Saving…");
        Add("editspec.save",                        "Lưu thay đổi",                             "Save changes");
    }
}
