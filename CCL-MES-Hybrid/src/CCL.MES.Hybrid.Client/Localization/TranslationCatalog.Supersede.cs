namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/SupersedeSpecModal.razor (supersede.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSupersede()
    {
        //     key                              vi                                                                    en
        Add("supersede.header",               "Đánh dấu Spec {0} là đã thay thế",                                    "Mark Spec {0} as superseded");
        Add("supersede.nosource",             "Chưa có Spec.",                                                       "No Spec yet.");
        Add("supersede.helper.pre",           "Đánh dấu Spec này là",                                                "Marking this Spec as");
        Add("supersede.helper.superseded",    "Đã thay thế",                                                         "Superseded");
        Add("supersede.helper.post",          "sẽ khóa việc phát hành tiếp theo. Hành động này không tự hoàn tác (bạn phải tạo bản Sửa đổi mới để quay lại quy trình).",
                                              "will lock further release. This action does not undo automatically (you must create a new Revise to return to the workflow).");
        Add("supersede.confirm.pre",          "Nhập chính xác mã Spec",                                              "Type the Spec code exactly");
        Add("supersede.confirm.post",         "để xác nhận",                                                         "to confirm");
        Add("supersede.cancel",               "Hủy",                                                                 "Cancel");
        Add("supersede.marking",              "Đang đánh dấu…",                                                      "Marking…");
        Add("supersede.mark",                 "Đánh dấu đã thay thế",                                                "Mark Superseded");
    }
}
