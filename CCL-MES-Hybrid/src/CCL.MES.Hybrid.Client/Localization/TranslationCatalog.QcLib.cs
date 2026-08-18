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

        // Smart platform — sub-tab, toolbar, row menu, info, add, flash.
        Add("qclib.line.label",          "Label",                                         "Label");
        Add("qclib.line.silk",           "Silk Screen",                                   "Silk Screen");
        Add("qclib.add",                 "Thêm mới",                                      "Add new");
        Add("qclib.import",              "Nhập (xlsx/csv)",                               "Import (xlsx/csv)");
        Add("qclib.export",              "Xuất CSV",                                       "Export CSV");
        Add("qclib.menu.actions",        "Hành động",                                     "Actions");
        Add("qclib.menu.info",           "Chi tiết",                                      "Info");
        Add("qclib.menu.copy",           "Sao chép",                                      "Copy");
        Add("qclib.menu.delete",         "Xoá",                                           "Delete");
        Add("qclib.info.title",          "Chi tiết hạng mục",                             "Item detail");
        Add("qclib.info.acceptance",     "Tiêu chuẩn chấp nhận",                          "Acceptance criteria");
        Add("qclib.info.methods",        "Method / process áp dụng",                      "Applied methods");
        Add("qclib.info.stages",         "Stage áp dụng",                                 "Applied stages");
        Add("qclib.info.condition",      "Điều kiện",                                      "Condition");
        Add("qclib.add.title",           "Thêm hạng mục kiểm",                            "Add check item");
        Add("qclib.add.save",            "Lưu",                                           "Save");
        Add("qclib.flash.deleted",       "Đã xoá {0}.",                                   "Deleted {0}.");
        Add("qclib.flash.added",         "Đã lưu {0}.",                                   "Saved {0}.");
        Add("qclib.flash.imported",      "Import xong: +{0} mới · {1} cập nhật · tổng {2}.",
                                                                                          "Imported: +{0} new · {1} updated · {2} total.");
        Add("qclib.flash.exported",      "Đã xuất {0}.",                                  "Exported {0}.");
    }
}
