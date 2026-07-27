namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/ReviseSpecModal.razor (revisespec.*).
public sealed partial class TranslationCatalog
{
    private void RegisterReviseSpec()
    {
        //     key                              vi                                                                   en
        Add("revisespec.header",             "Tạo bản Revise của Spec {0}",                                        "Create Revise of Spec {0}");
        Add("revisespec.no.source",          "Chưa có Spec nguồn.",                                                "No source Spec yet.");
        Add("revisespec.helper.pre",         "Một bản nháp mới sẽ được tạo + nguồn ({0} Rev {1}) sẽ tự động được đánh dấu",
                                             "A new draft will be created + the source ({0} Rev {1}) will automatically be marked");
        Add("revisespec.helper.superseded",  "Đã thay thế",                                                        "Superseded");
        Add("revisespec.helper.post",        ". Hành động này không thể tự động hoàn tác.",                        ". This action cannot be undone automatically.");
        Add("revisespec.reason.label",       "Lý do revise",                                                       "Revise reason");
        Add("revisespec.reason.placeholder", "Tối thiểu 5 ký tự",                                                  "At least 5 characters");
        Add("revisespec.reason.counter",     "{0} / 5 ký tự tối thiểu",                                            "{0} / 5 characters minimum");
        Add("revisespec.cancel",             "Hủy",                                                                "Cancel");
        Add("revisespec.submitting",         "Đang tạo Revise…",                                                   "Creating Revise…");
        Add("revisespec.submit",             "Tạo Revise",                                                         "Create Revise");
    }
}
