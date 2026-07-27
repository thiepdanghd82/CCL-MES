namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/Settings.razor (settings.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSettings()
    {
        //     key                            vi                                                        en
        Add("settings.pagetitle",          "Cài đặt — CCL MES",                                       "Settings — CCL MES");
        Add("settings.title",              "Cài đặt",                                                 "Settings");
        Add("settings.subtitle",           "Quản lý hồ sơ, mật khẩu và cấu hình trạm của bạn.",        "Manage your profile, password, and station configuration.");

        Add("settings.group.account",      "Tài khoản",                                               "Account");
        Add("settings.profile.title",      "Hồ sơ của tôi",                                           "My Profile");
        Add("settings.profile.sub",        "Tên hiển thị, vai trò, bộ phận.",                          "Display name, role, department.");
        Add("settings.password.title",     "Đổi mật khẩu",                                            "Change Password");
        Add("settings.password.sub",       "Đặt mật khẩu mới cho tài khoản hiện tại.",                 "Set a new password for the current account.");

        Add("settings.group.station",      "Trạm",                                                    "Station");
        Add("settings.connection.title",   "Kết nối",                                                 "Connection");
        Add("settings.connection.sub",     "Máy chủ, mạng, chế độ trạm — xem nhanh.",                  "Server, network, station mode — quick view.");
        Add("settings.about.title",        "Giới thiệu",                                              "About");
        Add("settings.about.sub",          "Phiên bản, dữ liệu, bộ nhớ.",                              "Version, data, storage.");
        Add("settings.devices.title",      "Thiết bị",                                                "Devices");
        Add("settings.devices.sub",        "Máy quét, máy in, cân — cấu hình ở P10.3.",                "Scanner, printer, scale — configured in P10.3.");
        Add("settings.stationmode.title",  "Chế độ trạm",                                             "Station Mode");
        Add("settings.stationmode.sub",    "Tương tác / Kiosk + mã khóa.",                             "Interactive / Kiosk + lock passcode.");

        Add("settings.group.appearance",   "Giao diện",                                               "Appearance");
        Add("settings.appearance.title",   "Giao diện",                                               "Appearance");
        Add("settings.appearance.sub",     "Ngôn ngữ Tiếng Việt / Tiếng Anh; giao diện sẽ có ở P10.6g.", "Vietnamese / English language; themes arriving in P10.6g.");

        Add("settings.group.administrator", "Quản trị viên",                                          "Administrator");
        Add("settings.backup.title",       "Sao lưu & Khôi phục",                                     "Backup & Restore");
        Add("settings.backup.sub",         "Ảnh chụp SQLite + khôi phục từ tệp.",                      "SQLite snapshot + restore from file.");
        Add("settings.audit.title",        "Nhật ký kiểm toán",                                       "Audit Log");
        Add("settings.audit.sub",          "Tìm kiếm + xuất CSV/XLSX.",                                "Search + export CSV/XLSX.");
        Add("settings.accounts.title",     "Quản lý tài khoản",                                       "Account Control");
        Add("settings.accounts.sub",       "Tạo / sửa / vô hiệu hóa / đặt lại mật khẩu.",              "Create / edit / disable / reset password.");
    }
}
