namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/ScannerTestPanel.razor (scanner.*).
public sealed partial class TranslationCatalog
{
    private void RegisterScanner()
    {
        //     key                            vi                                        en
        Add("scanner.checking",           "Đang kiểm tra…",                          "Checking…");
        Add("scanner.test.scan",          "Quét thử",                                "Test scan");
        Add("scanner.opening.camera",     "Đang mở camera…",                         "Opening camera…");
        Add("scanner.payload",            "Nội dung",                                "Payload");
        Add("scanner.format",             "Định dạng",                               "Format");
        Add("scanner.camera",             "Camera",                                  "Camera");
        Add("scanner.at",                 "Lúc",                                     "At");
        Add("scanner.cancelled",          "Đã hủy.",                                 "Cancelled.");
        Add("scanner.open.settings",      "Mở Cài đặt hệ thống",                     "Open System Settings");
        Add("scanner.opening",            "Đang mở…",                                "Opening…");
        Add("scanner.open.settings.help", "Không thể tự động mở Cài đặt. Vào Apple Menu → System Settings → Privacy & Security → Camera để cấp quyền cho CCL MES.",
                                          "Could not open Settings automatically. Go to Apple Menu → System Settings → Privacy & Security → Camera to grant permission for CCL MES.");
    }
}
