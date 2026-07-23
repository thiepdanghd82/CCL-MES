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

        // Batch 2B — FloatingWindow traffic-light tooltips + connectivity banner.
        Add("common.minimize",      "Thu nhỏ",             "Minimize");
        Add("common.maximize",      "Phóng to / khôi phục","Maximize / restore");
        Add("common.close",         "Đóng",                "Close");
        Add("common.close.esc",     "Đóng (Esc)",          "Close (Esc)");
        Add("conn.offline",         "Mất kết nối — đang chờ mạng để tải dữ liệu.",
                                    "Connection lost — waiting for the network to load data.");
    }
}
