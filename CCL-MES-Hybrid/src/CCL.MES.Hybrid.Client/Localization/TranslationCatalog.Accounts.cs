namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/SettingsAccounts.razor (accounts.*).
public sealed partial class TranslationCatalog
{
    private void RegisterAccounts()
    {
        //     key                              vi                                                                        en
        Add("accounts.pagetitle",              "Quản lý tài khoản — CCL MES",                                             "Account Control — CCL MES");
        Add("accounts.title",                  "Quản lý tài khoản",                                                       "Account Control");

        // ── List header + toolbar ──────────────────────────────────────
        Add("accounts.users.count",            "Người dùng (tổng {0})",                                                   "Users ({0} total)");
        Add("accounts.search.placeholder",     "Tìm theo tên đăng nhập / tên hiển thị / vai trò…",                        "Search by username / display / role…");
        Add("accounts.create.button",          "+ Tạo tài khoản",                                                         "+ Create account");
        Add("accounts.loading",                "Đang tải…",                                                               "Loading…");
        Add("accounts.empty",                  "Không có tài khoản nào khớp.",                                            "No matching accounts.");

        // ── Table columns ──────────────────────────────────────────────
        Add("accounts.col.username",           "Tên đăng nhập",                                                           "Username");
        Add("accounts.col.display",            "Tên hiển thị",                                                            "Display name");
        Add("accounts.col.role",               "Vai trò",                                                                 "Role");
        Add("accounts.col.department",         "Phòng ban",                                                               "Department");
        Add("accounts.col.status",             "Trạng thái",                                                              "Status");
        Add("accounts.col.changepw",           "Đổi mật khẩu?",                                                           "Change PW?");
        Add("accounts.col.lastlogin",          "Đăng nhập gần nhất",                                                      "Last login");
        Add("accounts.col.actions",            "Thao tác",                                                                "Actions");

        // ── Row cells ──────────────────────────────────────────────────
        Add("accounts.self",                   "(bạn)",                                                                   "(you)");
        Add("accounts.status.active",          "Đang hoạt động",                                                          "Active");
        Add("accounts.status.disabled",        "Đã vô hiệu",                                                              "Disabled");

        // ── Row action buttons + tooltips ─────────────────────────────
        Add("accounts.action.reset",           "Đặt lại",                                                                 "Reset");
        Add("accounts.action.reset.title",     "Đặt lại mật khẩu",                                                        "Reset password");
        Add("accounts.action.disable",         "Vô hiệu",                                                                 "Disable");
        Add("accounts.action.disable.title",   "Vô hiệu hóa",                                                             "Disable");
        Add("accounts.action.enable",          "Kích hoạt",                                                               "Enable");
        Add("accounts.action.enable.title",    "Kích hoạt lại",                                                           "Re-enable");
        Add("accounts.action.delete",          "Xóa",                                                                     "Delete");
        Add("accounts.action.delete.title",    "Xóa tài khoản vĩnh viễn",                                                 "Delete account permanently");

        // ── Paging ─────────────────────────────────────────────────────
        Add("accounts.paging.prev",            "‹ Trước",                                                                 "‹ Previous");
        Add("accounts.paging.pos",             "Trang {0} / {1}",                                                         "Page {0} / {1}");
        Add("accounts.paging.next",            "Sau ›",                                                                   "Next ›");

        // ── Create modal ───────────────────────────────────────────────
        Add("accounts.create.title",           "Tạo tài khoản mới",                                                       "Create new account");
        Add("accounts.field.username",         "Tên đăng nhập *",                                                         "Username *");
        Add("accounts.field.display",          "Tên hiển thị",                                                            "Display name");
        Add("accounts.field.role",             "Vai trò *",                                                               "Role *");
        Add("accounts.field.department",       "Phòng ban",                                                               "Department");
        Add("accounts.field.temppassword",     "Mật khẩu tạm thời (người dùng sẽ buộc phải đổi khi đăng nhập lần đầu) *", "Temporary password (the operator will be forced to change it on first sign-in) *");

        // ── Reset modal ────────────────────────────────────────────────
        Add("accounts.reset.title",            "Đặt lại mật khẩu — {0}",                                                  "Reset password — {0}");
        Add("accounts.reset.help",             "Người dùng sẽ buộc phải đổi mật khẩu ở lần đăng nhập kế tiếp (MustChangePassword=true). Phiên hiện tại của họ KHÔNG bị hủy — nếu cần hủy phiên, hãy dùng nút Vô hiệu rồi Kích hoạt lại.", "The user will be forced to change their password at the next sign-in (MustChangePassword=true). Their current session is NOT revoked — if you need to revoke the session, use the Disable button and then Enable again.");
        Add("accounts.field.newpassword",      "Mật khẩu tạm thời mới *",                                                 "New temporary password *");

        // ── Modal buttons ──────────────────────────────────────────────
        Add("accounts.button.cancel",          "Hủy",                                                                     "Cancel");
        Add("accounts.button.create",          "Tạo",                                                                     "Create");
        Add("accounts.button.creating",        "Đang tạo…",                                                               "Creating…");
        Add("accounts.button.reset",           "Đặt lại",                                                                 "Reset");
        Add("accounts.button.resetting",       "Đang đặt lại…",                                                           "Resetting…");

        // ── Confirm dialogs ────────────────────────────────────────────
        Add("accounts.confirm.enable",         "Kích hoạt lại tài khoản '{0}'?",                                          "Re-enable account '{0}'?");
        Add("accounts.confirm.disable",        "Vô hiệu hóa '{0}'? Toàn bộ refresh token của người dùng này sẽ bị thu hồi ngay lập tức; access token vẫn hiệu lực tối đa 15 phút.", "Disable '{0}'? All of this user's refresh tokens will be revoked immediately; access tokens remain valid for up to 15 minutes.");
        Add("accounts.confirm.delete",         "Xóa vĩnh viễn tài khoản '{0}' ({1})? Không thể hoàn tác. Lịch sử kiểm toán được giữ lại, nhưng thông tin đăng nhập bị xóa vĩnh viễn.", "Permanently delete account '{0}' ({1})? This cannot be undone. Their audit history is kept, but the login is removed for good.");

        // ── Success / info banners ─────────────────────────────────────
        Add("accounts.info.deleted",           "Đã xóa tài khoản {0}.",                                                 "Deleted account {0}.");
        Add("accounts.info.updated",           "Đã cập nhật {0}.",                                                      "Updated {0}.");
        Add("accounts.info.created",           "Đã tạo {0} ({1}). Người dùng sẽ buộc phải đổi mật khẩu ở lần đăng nhập đầu tiên.", "Created {0} ({1}). The user will be forced to change their password on first sign-in.");
        Add("accounts.info.reset",             "Đã đặt lại mật khẩu cho {0}. MustChangePassword = true.",               "Reset password for {0}. MustChangePassword = true.");

        // ── Error banners ──────────────────────────────────────────────
        Add("accounts.error.load",             "Tải thất bại: {0} ({1}).",                                                "Failed to load: {0} ({1}).");
        Add("accounts.error.delete",           "Xóa thất bại: {0} ({1}).",                                                "Delete failed: {0} ({1}).");
        Add("accounts.error.update",           "Cập nhật thất bại: {0} ({1}).",                                           "Update failed: {0} ({1}).");
        Add("accounts.error.modal",            "{0} ({1}).",                                                              "{0} ({1}).");
        Add("accounts.error.generic",          "Lỗi: {0} — {1}",                                                          "Error: {0} — {1}");
    }
}
