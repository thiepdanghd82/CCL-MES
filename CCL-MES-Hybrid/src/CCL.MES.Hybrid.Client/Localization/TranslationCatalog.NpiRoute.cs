namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/NpiRoutine.razor (npiroute.*).
public sealed partial class TranslationCatalog
{
    private void RegisterNpiRoute()
    {
        //     key                          vi                                          en
        Add("npiroute.pagetitle",          "Công đoạn — NPI",                          "Routing — NPI");
        Add("npiroute.heading",            "Công đoạn thao tác",                       "Operation Routing");
        Add("npiroute.search.placeholder", "Tìm theo mã hàng / công đoạn / trung tâm sản xuất…", "Search by part / op / work center…");
        Add("npiroute.search",             "Tìm kiếm",                                 "Search");
        Add("npiroute.reload",             "Tải lại",                                  "Reload");
        Add("npiroute.import",             "Nhập dữ liệu…",                            "Import…");
        Add("npiroute.loading",            "Đang tải…",                                "Loading…");
        Add("npiroute.error.title",        "Không tải được dữ liệu.",                  "Failed to load data.");
        Add("npiroute.retry",              "Thử lại",                                  "Retry");
        Add("npiroute.empty",              "Không có dữ liệu phù hợp.",                "No matching data.");
        Add("npiroute.pager.prev",         "Trước",                                    "Previous");
        Add("npiroute.pager.next",         "Sau",                                      "Next");
        Add("npiroute.pager.info",         "Trang {0} / {1} — {2} dòng",               "Page {0} / {1} — {2} rows");
    }
}
