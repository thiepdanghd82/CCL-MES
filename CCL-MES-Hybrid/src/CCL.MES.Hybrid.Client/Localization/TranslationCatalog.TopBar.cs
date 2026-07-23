namespace CCL.MES.Hybrid.Client.Localization;

// Batch 1 — global top bar (TopBar.razor).
public sealed partial class TranslationCatalog
{
    private void RegisterTopBar()
    {
        //     key                    vi                    en
        Add("topbar.user",         "Người dùng",          "User");
        Add("topbar.shift",        "Ca",                  "Shift");
        Add("topbar.time",         "Giờ",                 "Time");
        Add("topbar.lang.switch",  "Đổi ngôn ngữ",        "Switch language");
    }
}
