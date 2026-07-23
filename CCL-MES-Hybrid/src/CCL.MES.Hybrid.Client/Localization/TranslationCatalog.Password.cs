namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/SettingsPassword.razor (password.*).
public sealed partial class TranslationCatalog
{
    private void RegisterPassword()
    {
        //     key                            vi                                                              en
        Add("password.pagetitle",         "Đổi mật khẩu — CCL MES",                                        "Change Password — CCL MES");
        Add("password.title",             "Đổi mật khẩu",                                                  "Change Password");

        Add("password.field.current",     "Mật khẩu hiện tại",                                             "Current password");
        Add("password.field.new",         "Mật khẩu mới",                                                  "New password");
        Add("password.field.confirm",     "Xác nhận mật khẩu mới",                                         "Confirm new password");
        Add("password.hint.minlength",    "Ít nhất 4 ký tự.",                                              "At least 4 characters.");

        Add("password.button.submit",     "Đổi mật khẩu",                                                  "Change Password");
        Add("password.button.changing",   "Đang đổi…",                                                     "Changing…");

        Add("password.error.missing_fields", "Vui lòng điền đầy đủ các trường.",                           "Please fill in all fields.");
        Add("password.error.too_short",   "Mật khẩu mới phải có ít nhất 4 ký tự.",                         "The new password must be at least 4 characters.");
        Add("password.error.mismatch",    "Mật khẩu mới và xác nhận không khớp.",                          "The new password and confirmation do not match.");
        Add("password.success",           "✓ Đã đổi mật khẩu. Dùng mật khẩu mới cho lần đăng nhập sau.",   "✓ Password changed. Use the new password the next time you sign in.");
        Add("password.error.connect",     "Không thể kết nối tới máy chủ: {0}",                            "Could not connect to the server: {0}");
        Add("password.error.failed",      "Đổi mật khẩu thất bại: {0}",                                    "Password change failed: {0}");
    }
}
