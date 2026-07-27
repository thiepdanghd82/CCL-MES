namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — SettingDashboard.razor (setting.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSetting()
    {
        //     key                              vi                                                      en
        Add("setting.loading",                 "Đang tải trạng thái SETTING…",                          "Loading SETTING status…");
        Add("setting.error.load",              "Không tải được trạng thái SETTING:",                    "Could not load SETTING status:");
        Add("setting.error.save",              "Không lưu được thay đổi:",                              "Could not save change:");
        Add("setting.dismiss",                 "Bỏ qua",                                                "Dismiss");

        Add("setting.invalidphase.title",      "Lệnh SX không ở bước SETTING",                          "WO is not in the SETTING phase");
        Add("setting.invalidphase.current",    "Hiện tại:",                                             "Current:");
        Add("setting.invalidphase.hint",       "Quay lại tab Lệnh SX để chọn lệnh khác.",               "Go back to the Work Orders tab to select another WO.");

        Add("setting.timer.label",             "Thời gian setting",                                     "Setting time");

        Add("setting.checklist.title",         "Danh mục kiểm tra setting",                             "Setting checklist");
        Add("setting.checklist.confirmed",     "Đã xác nhận {0}/{1}",                                   "{0}/{1} confirmed");

        Add("setting.checklist.item0",         "Lắp khuôn / bản in đúng vị trí, siết chặt",             "Mount die / plate in the correct position, tightened");
        Add("setting.checklist.item1",         "Lắp dao cắt đúng khổ, kiểm tra lưỡi dao",               "Mount the correct-size cutter, inspect the blade");
        Add("setting.checklist.item2",         "Mực + vật tư đúng đơn hàng, pha sẵn và sẵn sàng",       "Ink + materials matching the order, premixed and ready");
        Add("setting.checklist.item3",         "Canh màu / căn chỉnh chồng khít hoàn tất",              "Color registration / alignment complete");
        Add("setting.checklist.item4",         "Chạy mẫu đầu tiên — đối chiếu với spec",                "Run a first sample — compare against spec");
        Add("setting.checklist.item5",         "Vệ sinh máy + an toàn khu vực trước khi chạy",          "Machine cleaned + workplace safety before running");

        Add("setting.done.button",             "Hoàn tất Setting (chuyển sang IPQC)",                   "Finish Setting (advance to IPQC)");
        Add("setting.action.hint",             "Tích đủ {0} mục → nút sẽ bật lên.",                      "Check all {0} items → the button becomes active.");
    }
}
