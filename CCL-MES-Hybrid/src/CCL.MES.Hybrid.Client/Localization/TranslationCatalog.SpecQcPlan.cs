namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecQcPlansTab.razor (specqcplan.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecQcPlan()
    {
        //     key                            vi                                                     en
        Add("specqcplan.empty",            "Chưa có dữ liệu QC.",                                  "No QC data yet.");

        // Read-only banner — role name renders in a <strong> between the two halves.
        Add("specqcplan.readonly.prefix",  "Tài khoản hiện tại (vai trò",                          "The current account (role");
        Add("specqcplan.readonly.suffix",  ") chỉ có thể xem kế hoạch QC. Cần quyền Admin hoặc Engineer để chỉnh sửa.",
                                           ") can only view QC plans. Admin or Engineer is required to edit.");

        Add("specqcplan.count",            "{0} tiêu chí",                                         "{0} criteria");
        Add("specqcplan.addrow",           "+ Thêm dòng",                                          "+ Add row");
        Add("specqcplan.saving",           "Đang lưu…",                                            "Saving…");
        Add("specqcplan.savestage",        "Lưu công đoạn",                                        "Save stage");

        // Table column headers.
        Add("specqcplan.col.criterion",    "Tiêu chí",                                             "Criterion");
        Add("specqcplan.col.target",       "Mục tiêu",                                             "Target");
        Add("specqcplan.col.tolerance",    "Dung sai",                                             "Tolerance");
        Add("specqcplan.col.method",       "Phương pháp",                                          "Method");
        Add("specqcplan.col.frequency",    "Tần suất",                                             "Frequency");

        Add("specqcplan.nocriteria",       "— Chưa có tiêu chí —",                                 "— No criteria yet —");
        Add("specqcplan.delrow",           "Xóa dòng",                                             "Delete row");

        // Stage labels — IPQC / FQC / OQC kept as technical tokens; suffix translated.
        Add("specqcplan.stage.ipqcprint",  "IPQC · In",                                            "IPQC · Print");
        Add("specqcplan.stage.ipqccut",    "IPQC · Cắt",                                           "IPQC · Cut");
        Add("specqcplan.stage.fqc",        "FQC · Cuối",                                           "FQC · Final");
        Add("specqcplan.stage.oqc",        "OQC · Xuất hàng",                                      "OQC · Outgoing");

        // Messages.
        Add("specqcplan.err.nameempty",    "Tên tiêu chí không được để trống.",                    "The criterion name cannot be empty.");
        Add("specqcplan.saved",            "Đã lưu {0} tiêu chí cho công đoạn {1}.",               "Saved {0} criteria for stage {1}.");
        Add("specqcplan.err.connect",      "Không thể kết nối tới máy chủ: {0}",                   "Could not connect to the server: {0}");
    }
}
