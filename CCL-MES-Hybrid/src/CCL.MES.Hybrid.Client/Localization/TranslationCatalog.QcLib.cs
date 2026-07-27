namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/QcLibrary.razor (qclib.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQcLib()
    {
        //     key                          vi                                              en
        Add("qclib.pagetitle",           "QC Library — Thư viện hạng mục kiểm",           "QC Library — Check Item Library");
        Add("qclib.heading",             "Thư viện hạng mục kiểm (QC Library)",           "Check Item Library (QC Library)");
        Add("qclib.subtitle",            "Master data auto-sync theo process line. Sửa qua import idempotent.",
                                                                                          "Master data auto-synced per process line. Edit via idempotent import.");
        Add("qclib.filter.line",         "Process line:",                                 "Process line:");
        Add("qclib.filter.all",          "— Tất cả —",                                    "— All —");
        Add("qclib.search.placeholder",  "Tìm theo mã / nội dung…",                       "Search by code / content…");
        Add("qclib.reload",              "Tải lại",                                       "Reload");
        Add("qclib.error.load",          "Lỗi tải thư viện.",                             "Failed to load library.");
        Add("qclib.loading",             "Đang tải…",                                     "Loading…");
        Add("qclib.count",               "{0} hạng mục",                                  "{0} items");
        Add("qclib.col.line",            "Line",                                          "Line");
        Add("qclib.col.group",           "Nhóm",                                          "Group");
        Add("qclib.col.content",         "Nội dung",                                      "Content");
        Add("qclib.col.acceptance",      "Chấp nhận",                                     "Acceptance");
    }
}
