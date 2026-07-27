namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecDiffViewer.razor (specdiff.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecDiff()
    {
        //     key                             vi                                                          en
        Add("specdiff.empty",                "Không có dữ liệu so sánh.",                                 "No comparison data.");

        Add("specdiff.nodiff.title",         "Không có khác biệt",                                        "No differences");
        Add("specdiff.nodiff.between",       "giữa rev {0} và rev {1}.",                                  "between rev {0} and rev {1}.");

        Add("specdiff.comparing.prefix",     "Đang so sánh rev",                                          "Comparing rev");
        Add("specdiff.comparing.current.with", "(hiện tại) với rev",                                      "(current) with rev");
        Add("specdiff.comparing.previous",   "(trước đó) —",                                              "(previous) —");
        Add("specdiff.comparing.changes",    "thay đổi",                                                  "changes");

        Add("specdiff.col.field",            "Trường",                                                    "Field");
        Add("specdiff.col.before",           "Trước (rev {0})",                                           "Before (rev {0})");
        Add("specdiff.col.after",            "Sau (rev {0})",                                             "After (rev {0})");
        Add("specdiff.col.type",             "Loại",                                                      "Type");

        Add("specdiff.section.identity",     "Nhận dạng / Đầu spec",                                      "Identity / Header");
        Add("specdiff.section.printparams",  "Thông số in",                                               "Print Parameters");
        Add("specdiff.section.material",     "Vật tư",                                                    "Material");
        Add("specdiff.section.remarks",      "Ghi chú",                                                   "Remarks");
        Add("specdiff.section.printcolors",  "Màu lụa",                                                   "Silk Colours");
        Add("specdiff.section.flexoprint",   "Dòng in Flexo",                                             "Flexo Print Rows");
        Add("specdiff.section.flexocutting", "Dòng bế Flexo",                                             "Flexo Cutting Rows");
        Add("specdiff.section.flexoink",     "Dòng mực Flexo",                                            "Flexo Ink Rows");

        Add("specdiff.kind.added",           "Thêm mới",                                                  "Added");
        Add("specdiff.kind.removed",         "Đã xóa",                                                    "Removed");
        Add("specdiff.kind.changed",         "Thay đổi",                                                  "Changed");
    }
}
