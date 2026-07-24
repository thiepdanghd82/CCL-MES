namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/Mode.razor (mode.*).
public sealed partial class TranslationCatalog
{
    private void RegisterMode()
    {
        //     key                          vi                                                                                              en
        Add("mode.page.title",           "Chế độ máy trạm — CCL MES",                                                                     "Mode — CCL MES");
        Add("mode.heading",              "Chế độ máy trạm",                                                                               "Station Mode");

        Add("mode.gate.title",           "Tính năng phần cứng đang tắt.",                                                                 "Hardware features are turned off.");
        Add("mode.gate.body",            "Trang /mode bật cùng với /hardware khi cờ Hardware:ScanEnabled = true được thiết lập. W1 chỉ dựng khung; logic kiosk + màn hình khóa sẽ có ở W4.",
                                         "The /mode page turns on together with /hardware when the Hardware:ScanEnabled = true flag is set. W1 only renders the frame; the kiosk + lock-screen logic ships in W4.");

        Add("mode.station.label",        "Máy trạm:",                                                                                     "Station:");

        Add("mode.section.mode",         "Chế độ",                                                                                        "Mode");
        Add("mode.interactive",          "Tương tác",                                                                                     "Interactive");
        Add("mode.kiosk",                "Kiosk",                                                                                         "Kiosk");
        Add("mode.headless",             "Không giao diện",                                                                               "Headless");
        Add("mode.headless.reserved",    "Không giao diện (P10.4+)",                                                                      "Headless (P10.4+)");

        Add("mode.help.interactive",     "chế độ toàn màn hình, không tự khóa. Dùng tại bàn văn phòng / NPI / trưởng ca.",
                                         "mode — full screen, no auto lock. Used at the office desk / NPI / supervisor.");
        Add("mode.help.kiosk",           "chế độ ẩn thanh bên sau lần điều hướng đầu tiên; sau N phút không hoạt động → khóa màn hình (nhập mã để mở khóa). JWT không bị thu hồi — chỉ chặn truy cập giao diện. Mặc định N=10 phút.",
                                         "mode — hides the sidebar after the first navigation; after N minutes of inactivity → locks the screen (enter the passcode to unlock). The JWT is not revoked — only UI access is blocked. Default N=10 minutes.");
        Add("mode.help.headless",        "chế độ chỉ chạy máy quét, không giao diện. Dành riêng cho P10.4+.",
                                         "mode — scanner only, no UI. Reserved for P10.4+.");

        Add("mode.section.idle",         "Ngưỡng nghỉ (phút)",                                                                            "Idle threshold (minutes)");
        Add("mode.help.idle",            "Áp dụng cho chế độ Kiosk. Phạm vi 1..120. Mặc định 10. Sau khi thay đổi, tự khóa trên máy trạm này dùng giá trị mới ngay lập tức (không cần khởi động lại ứng dụng).",
                                         "Applies to Kiosk. Range 1..120. Default 10. After a change, auto lock on this station uses the new value immediately (no need to restart the app).");

        Add("mode.section.passcode",     "Mã mở khóa thiết bị",                                                                           "Device passcode");
        Add("mode.help.passcode",        "Dùng để mở khóa màn hình khóa ở chế độ Kiosk. Đây KHÔNG phải mật khẩu tài khoản. Lưu trên thiết bị này (không đồng bộ lên máy chủ). Để trống để xóa.",
                                         "Used to unlock the lock-screen in Kiosk mode. This is NOT the account password. Stored on this device (not synced to the server). Leave blank to clear.");
        Add("mode.passcode.placeholder", "Nhập mã mới rồi bấm Lưu",                                                                       "Enter a new passcode then click Save");
        Add("mode.passcode.save",        "Lưu mã",                                                                                        "Save passcode");
        Add("mode.passcode.cleared",     "Đã xóa mã.",                                                                                    "Passcode cleared.");
        Add("mode.passcode.saved",       "Đã lưu mã.",                                                                                    "Passcode saved.");
    }
}
