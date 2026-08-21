namespace CCL.MES.Hybrid.Client.Localization;

// P2-PR1 — window-manager + taskbar surface. Window TITLE keys mirror
// WindowRegistryKeys.TitleKeys.* (the host binds those constants); taskbar chip
// tooltips + the soft-cap banner live here too. EN + VI parity (gate-i18n).
public sealed partial class TranslationCatalog
{
    private void RegisterTaskbar()
    {
        //     key                                 vi                                     en
        // Window titles (WindowRegistryKeys.TitleKeys.*)
        Add("windows.qc_history.title",          "Lịch sử QC",                          "QC History");
        Add("windows.shop_order_history.title",  "Lịch sử lệnh SX",                     "Shop Order History");
        Add("windows.machine_dashboard.title",   "Bảng điều khiển máy",                 "Machine Dashboard");
        Add("windows.qc_library.title",          "Thư viện QC",                         "QC Library");

        // Taskbar chip tooltips
        Add("taskbar.restore",                   "Khôi phục",                           "Restore");
        Add("taskbar.close",                     "Đóng",                                "Close");
        Add("taskbar.maximize",                  "Phóng to",                            "Maximize");
        Add("taskbar.empty",                     "Thanh cửa sổ",                        "Window taskbar");

        // Soft-cap banner — {0} = SoftCap (8)
        Add("window.max_reached",                "Đã đạt tối đa {0} cửa sổ",            "Maximum {0} windows reached");
    }
}
