namespace CCL.MES.Hybrid.Client.Localization;

// Batch 1 — cross-surface primitives shared by many pages.
public sealed partial class TranslationCatalog
{
    private void RegisterCommon()
    {
        //     key                     vi                   en
        Add("common.logout",        "Đăng xuất",          "Logout");
        Add("common.online",        "Trực tuyến",         "ONLINE");
        Add("common.back.settings", "‹ Cài đặt",          "‹ Settings");
        Add("common.language",      "Ngôn ngữ",           "Language");
    }
}
