namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/SettingsAbout.razor (about.*).
public sealed partial class TranslationCatalog
{
    private void RegisterAbout()
    {
        //     key                          vi                                 en
        Add("about.pagetitle",           "Giới thiệu — CCL MES",             "About — CCL MES");
        Add("about.title",               "Giới thiệu",                       "About");
        Add("about.loading",             "Đang tải thông tin máy chủ…",       "Loading server information…");
        Add("about.load.failed",         "Không tải được trang Giới thiệu.",  "Failed to load About.");
        Add("about.retry",               "Thử lại",                          "Try again");

        Add("about.server",              "Máy chủ",                          "Server");
        Add("about.version",             "Phiên bản",                        "Version");
        Add("about.build",               "Bản dựng",                         "Build");
        Add("about.environment",         "Môi trường",                       "Environment");
        Add("about.server.machine",      "Máy chủ",                          "Server");
        Add("about.servertime.utc",      "Giờ máy chủ (UTC)",                "Server time (UTC)");

        Add("about.data",                "Dữ liệu",                          "Data");
        Add("about.users",               "Người dùng",                       "Users");
        Add("about.customers",           "Khách hàng",                       "Customers");
        Add("about.products",            "Sản phẩm",                         "Products");
        Add("about.specrevisions",       "Phiên bản spec",                   "Spec revisions");
        Add("about.drawingversions",     "Phiên bản bản vẽ",                 "Drawing versions");
        Add("about.auditentries",        "Bản ghi nhật ký",                  "Audit entries");

        Add("about.storage",             "Lưu trữ trên đĩa",                 "On-disk storage");
        Add("about.database",            "Cơ sở dữ liệu",                    "Database");
        Add("about.drawingstore",        "Kho bản vẽ",                       "Drawing store");
        Add("about.datadir",             "Thư mục dữ liệu",                  "Data directory");

        Add("about.catalyst.hint",       "Phiên bản ứng dụng Catalyst được hiển thị trong Cài đặt → Kết nối.", "The Catalyst app version is shown in Settings → Connection.");
    }
}
