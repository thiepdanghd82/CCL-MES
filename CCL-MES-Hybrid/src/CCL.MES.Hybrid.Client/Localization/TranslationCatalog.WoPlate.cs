namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — WoPlateCheck.razor (woplate.*).
public sealed partial class TranslationCatalog
{
    private void RegisterWoPlate()
    {
        //     key                          vi                                          en
        Add("woplate.section.title",        "Kiểm bản kẽm",                              "Plate check");
        Add("woplate.status.pending",       "Chưa kiểm",                                 "Not checked");

        Add("woplate.plateno.label",        "Số/mã bản kẽm",                             "Plate number/code");
        Add("woplate.plateno.aria",         "Số bản kẽm",                                "Plate number");

        Add("woplate.ngreason.label",       "Mã lỗi NG (phế)",                           "NG reason code (scrap)");
        Add("woplate.ngreason.aria",        "Mã lỗi NG bản kẽm",                         "Plate NG reason code");
        Add("woplate.ngreason.placeholder", "— chọn mã lỗi —",                           "— select defect code —");

        Add("woplate.ngnote.label",         "Ghi chú NG (1-500 ký tự)",                  "NG note (1-500 characters)");
        Add("woplate.ngnote.aria",          "Ghi chú NG bản kẽm",                        "Plate NG note");

        Add("woplate.btn.ok",               "Ghi nhận bản kẽm OK",                       "Record plate OK");
        Add("woplate.btn.ng.arm",           "Đánh NG",                                   "Mark NG");
        Add("woplate.btn.ng.save",          "Lưu NG",                                    "Save NG");
        Add("woplate.btn.cancel",           "Hủy",                                       "Cancel");
        Add("woplate.ng.empty.title",       "Danh mục mã lỗi NG đang trống — báo IT",    "NG code catalog is empty — report to IT");

        Add("woplate.checkedby.label",      "Người kiểm:",                               "Checked by:");
    }
}
