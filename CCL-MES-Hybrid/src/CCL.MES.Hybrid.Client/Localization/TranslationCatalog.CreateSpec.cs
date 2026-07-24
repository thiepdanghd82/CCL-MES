namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/CreateSpecModal.razor (createspec.*).
public sealed partial class TranslationCatalog
{
    private void RegisterCreateSpec()
    {
        //     key                              vi                                                              en
        Add("createspec.header",             "Tạo Spec mới",                                                  "Create new Spec");

        Add("createspec.field.partno",       "Part No",                                                       "Part No");
        Add("createspec.field.partno.ph",    "vd. 1000527330 (mã part / IFS)",                                "e.g. 1000527330 (part / IFS no)");
        Add("createspec.dup.partno",         "Part No này đã có trên một spec đã lưu.",                       "This Part No is already on a saved spec.");

        Add("createspec.field.ifscode",      "Mã IFS",                                                        "IFS code");
        Add("createspec.field.ifscode.ph",   "vd. SP-001 hoặc mã tham chiếu khách hàng",                      "e.g. SP-001 or customer REF");
        Add("createspec.dup.ifscode",        "Mã IFS này đã có trên một spec đã lưu.",                        "This IFS code is already on a saved spec.");

        Add("createspec.field.spec",         "Spec",                                                          "Spec");
        Add("createspec.field.spec.ph",      "Số spec (đồng bộ sang spec sheet)",                             "Spec number (syncs to spec sheet)");
        Add("createspec.dup.spec",           "Số Spec này đã có trên một spec đã lưu.",                       "This Spec number is already on a saved spec.");

        Add("createspec.field.customer",     "Khách hàng",                                                    "Customer");
        Add("createspec.field.customer.ph",  "Tên khách hàng (đồng bộ sang spec sheet)",                      "Customer name (syncs to spec sheet)");

        Add("createspec.field.partname",     "Tên part",                                                      "Part Name");
        Add("createspec.field.partname.ph",  "Tên part / sản phẩm",                                           "Part / product name");

        Add("createspec.field.planner",      "Người lập kế hoạch",                                            "Planner");
        Add("createspec.field.planner.hint", "Tải mẫu spec-sheet tương ứng (Silkscreen / Flexo / Indigo / Letterpress / Diecut).",
                                             "Loads the matching spec-sheet template (Silkscreen / Flexo / Indigo / Letterpress / Diecut).");

        Add("createspec.reason.label",       "Lý do trùng lặp",                                               "Reason for the duplicate");
        Add("createspec.reason.ph",          "Vì sao tạo một spec trùng với spec đã có?",                     "Why create a spec that duplicates an existing one?");

        Add("createspec.btn.cancel",         "Hủy",                                                           "Cancel");
        Add("createspec.btn.creating",       "Đang tạo…",                                                     "Creating…");
        Add("createspec.btn.createanyway",   "Vẫn tạo",                                                       "Create anyway");
        Add("createspec.btn.create",         "Tạo Spec",                                                      "Create Spec");

        Add("createspec.error.connect",      "Không thể kết nối tới máy chủ: {0}",                            "Could not connect to the server: {0}");
    }
}
