namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/NpiStructures.razor (npistruct.*).
public sealed partial class TranslationCatalog
{
    private void RegisterNpiStruct()
    {
        //     key                              vi                                          en
        Add("npistruct.page.title",          "Cấu trúc (BOM) — NPI",                      "Structure (BOM) — NPI");
        Add("npistruct.heading",             "Cấu trúc (BOM)",                            "Structure (BOM)");
        Add("npistruct.search.placeholder",  "Tìm theo mã / mô tả…",                      "Search by part / description…");
        Add("npistruct.action.search",       "Tìm kiếm",                                  "Search");
        Add("npistruct.action.reload",       "Tải lại",                                   "Reload");
        Add("npistruct.action.import",       "Nhập dữ liệu…",                             "Import…");
        Add("npistruct.action.retry",        "Thử lại",                                   "Retry");
        Add("npistruct.status.loading",      "Đang tải…",                                 "Loading…");
        Add("npistruct.error.load_failed",   "Không tải được dữ liệu.",                   "Failed to load data.");
        Add("npistruct.grid.empty",          "Không có dữ liệu phù hợp.",                 "No matching data.");
        Add("npistruct.pager.previous",      "Trước",                                     "Previous");
        Add("npistruct.pager.next",          "Sau",                                       "Next");
        Add("npistruct.pager.status",        "Trang {0} / {1} — {2} dòng",                "Page {0} / {1} — {2} rows");
    }
}
