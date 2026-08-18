namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/SettingsProfile.razor (profile.*).
public sealed partial class TranslationCatalog
{
    private void RegisterProfile()
    {
        //     key                          vi                                           en
        Add("profile.pagetitle",         "Hồ sơ của tôi — CCL MES",                    "My Profile — CCL MES");
        Add("profile.title",             "Hồ sơ của tôi",                              "My Profile");

        Add("profile.loading",           "Đang tải hồ sơ…",                            "Loading profile…");
        Add("profile.load.failed",       "Không tải được hồ sơ.",                      "Failed to load profile.");
        Add("profile.load.retry",        "Thử lại",                                    "Try again");

        Add("profile.mustchange.prefix", "Bạn được cấp mật khẩu tạm thời. Vui lòng vào",
                                         "You were issued a temporary password. Please go to");
        Add("profile.mustchange.link",   "Đổi mật khẩu",                               "Change Password");
        Add("profile.mustchange.suffix", "để đặt mật khẩu của riêng bạn.",             "to set your own password.");

        Add("profile.field.username",    "Tên đăng nhập",                              "Username");
        Add("profile.hint.username",     "Liên hệ quản trị viên nếu bạn cần thay đổi.", "Contact your administrator if you need to change it.");
        Add("profile.field.role",        "Vai trò",                                    "Role");
        Add("profile.field.department",  "Bộ phận",                                    "Department");
        Add("profile.field.displayname", "Tên hiển thị",                               "Display name");
        Add("profile.hint.displayname",  "Tối đa 100 ký tự. Để trống để xóa.",         "Up to 100 characters. Leave blank to clear.");

        Add("profile.action.save",       "Lưu thay đổi",                               "Save changes");
        Add("profile.action.saving",     "Đang lưu…",                                  "Saving…");

        Add("profile.meta.created",      "Tạo lúc",                                    "Created");
        Add("profile.meta.modified",     "Sửa lần cuối",                               "Last modified");

        Add("profile.banner.saved",      "Đã lưu thay đổi.",                         "Changes saved.");
        Add("profile.error.connect",     "Không thể kết nối tới máy chủ: {0}",         "Could not connect to the server: {0}");
        Add("profile.error.save",        "Lưu thất bại: {0}",                          "Save failed: {0}");
    }
}
