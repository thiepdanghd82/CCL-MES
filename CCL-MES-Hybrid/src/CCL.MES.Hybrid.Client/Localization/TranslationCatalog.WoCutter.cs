namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — WoCutterCheck.razor (wocutter.*).
public sealed partial class TranslationCatalog
{
    private void RegisterWoCutter()
    {
        //     key                                vi                                          en
        Add("wocutter.section.title",         "Kiểm dao chặt",                             "Cutter check");
        Add("wocutter.status.notchecked",     "Chưa kiểm",                                 "Not checked");

        Add("wocutter.number.label",          "Số/mã dao chặt",                            "Cutter number/code");
        Add("wocutter.number.aria",           "Số dao chặt",                               "Cutter number");

        Add("wocutter.ng.reason.label",       "Mã lỗi NG (phế)",                           "NG reason code (scrap)");
        Add("wocutter.ng.reason.aria",        "Mã lỗi NG dao chặt",                        "Cutter NG reason code");
        Add("wocutter.ng.reason.placeholder", "— chọn mã lỗi —",                           "— select defect code —");

        Add("wocutter.ng.note.label",         "Ghi chú NG (1-500 ký tự)",                  "NG note (1-500 characters)");
        Add("wocutter.ng.note.aria",          "Ghi chú NG dao chặt",                       "Cutter NG note");

        Add("wocutter.btn.ok",                "Ghi nhận dao chặt OK",                      "Record cutter OK");
        Add("wocutter.btn.ng.arm",            "Đánh NG",                                   "Mark NG");
        Add("wocutter.btn.ng.save",           "Lưu NG",                                    "Save NG");
        Add("wocutter.btn.cancel",            "Hủy",                                        "Cancel");
        Add("wocutter.ng.catalog.empty",      "Danh mục mã lỗi NG trống — báo IT",         "NG code catalog is empty — report to IT");

        Add("wocutter.checkedby.label",       "Người kiểm:",                               "Checked by:");
    }
}
