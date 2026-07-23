namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2D — Pages/SettingsAuditLog.razor (auditlog.*).
public sealed partial class TranslationCatalog
{
    private void RegisterAuditLog()
    {
        //     key                                    vi                                                          en
        Add("auditlog.pagetitle",                 "Nhật ký kiểm toán — CCL MES",                               "Audit Log — CCL MES");
        Add("auditlog.title",                     "Nhật ký kiểm toán",                                         "Audit Log");

        Add("auditlog.filters.title",             "Bộ lọc",                                                    "Filters");
        Add("auditlog.filter.search",             "Tìm kiếm",                                                  "Search");
        Add("auditlog.filter.search.placeholder", "Tìm trên tất cả các cột…",                                  "Search across all columns…");
        Add("auditlog.filter.action",             "Hành động",                                                 "Action");
        Add("auditlog.filter.action.all",         "— Tất cả —",                                                "— All —");
        Add("auditlog.filter.user",               "Người dùng",                                                "User");
        Add("auditlog.filter.user.placeholder",   "admin, supervisor, …",                                      "admin, supervisor, …");
        Add("auditlog.filter.from",               "Từ ngày (UTC)",                                             "From date (UTC)");
        Add("auditlog.filter.to",                 "Đến ngày (UTC)",                                            "To date (UTC)");

        Add("auditlog.loading",                   "Đang tải…",                                                 "Loading…");
        Add("auditlog.apply",                     "Áp dụng + Tải lại",                                         "Apply + Reload");
        Add("auditlog.reset",                     "Đặt lại",                                                   "Reset");
        Add("auditlog.exporting",                 "Đang xuất…",                                                "Exporting…");
        Add("auditlog.export.csv",                "Xuất CSV",                                                  "Export CSV");
        Add("auditlog.export.xlsx",               "Xuất XLSX",                                                 "Export XLSX");

        Add("auditlog.results.head",              "Kết quả ({0} tổng — trang {1} / {2}, {3} dòng)",            "Results ({0} total — page {1} / {2}, {3} rows)");
        Add("auditlog.paging.prev",               "‹ Trước",                                                   "‹ Previous");
        Add("auditlog.paging.page",               "Trang {0}",                                                 "Page {0}");
        Add("auditlog.paging.next",               "Tiếp ›",                                                    "Next ›");
        Add("auditlog.empty",                     "Không có dòng nào khớp bộ lọc. Hãy mở rộng khoảng ngày hoặc xóa bộ lọc người dùng.",
                                                  "No rows match the filter. Try widening the date range or clearing the user filter.");

        Add("auditlog.col.utc",                   "UTC",                                                       "UTC");
        Add("auditlog.col.user",                  "Người dùng",                                                "User");
        Add("auditlog.col.role",                  "Vai trò",                                                   "Role");
        Add("auditlog.col.action",                "Hành động",                                                 "Action");
        Add("auditlog.col.target",                "Đối tượng",                                                 "Target");
        Add("auditlog.col.id",                    "ID",                                                        "ID");
        Add("auditlog.col.ip",                    "IP",                                                        "IP");
        Add("auditlog.col.source",                "Nguồn",                                                     "Source");
        Add("auditlog.col.detail",                "Chi tiết",                                                  "Detail");

        Add("auditlog.detail.empty.title",        "(trống)",                                                   "(empty)");
        Add("auditlog.detail.json.head.prefix",   "Chi tiết (JSON) — sự kiện",                                 "Detail (JSON) — event");
        Add("auditlog.detail.json.head.suffix",   "#{0}",                                                      "#{0}");
        Add("auditlog.copy",                      "Sao chép",                                                  "Copy");
        Add("auditlog.copy.done",                 "✔ Đã sao chép",                                             "✔ Copied");
    }
}
