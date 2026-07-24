namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/Lock.razor (lock.*).
public sealed partial class TranslationCatalog
{
    private void RegisterLock()
    {
        //     key                          vi                                                en
        Add("lock.pagetitle",            "Đã khoá — CCL MES",                               "Locked — CCL MES");
        Add("lock.title",                "Trạm đã khoá",                                    "Station Locked");
        Add("lock.sub",                  "Nhập mật mã thiết bị để mở khoá.",               "Enter the device passcode to unlock.");
        Add("lock.passcode.placeholder", "Mật mã",                                          "Passcode");
        Add("lock.checking",             "Đang kiểm tra…",                                  "Checking…");
        Add("lock.unlock",               "Mở khoá",                                         "Unlock");
        Add("lock.login.other",          "Đăng nhập bằng tài khoản khác",                   "Log in with a different account");
        Add("lock.error.empty",          "Chưa nhập mật mã.",                               "Passcode is empty.");
        Add("lock.error.incorrect",      "Mật mã không đúng.",                              "Incorrect passcode.");
    }
}
