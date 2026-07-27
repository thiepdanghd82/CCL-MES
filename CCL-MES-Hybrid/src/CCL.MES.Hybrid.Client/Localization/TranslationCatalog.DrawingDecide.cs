namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/DrawingDecideModal.razor (drawingdecide.*).
public sealed partial class TranslationCatalog
{
    private void RegisterDrawingDecide()
    {
        //     key                                vi                                                                         en
        Add("drawingdecide.help.prefix",       "Duyệt chip",                                                               "Approve the");
        Add("drawingdecide.help.chipfor",      "cho",                                                                      "chip for");

        Add("drawingdecide.decision",          "Quyết định",                                                               "Decision");
        Add("drawingdecide.approve",           "Duyệt",                                                                    "Approve");
        Add("drawingdecide.reject",            "Từ chối",                                                                  "Reject");

        Add("drawingdecide.reason",            "Lý do",                                                                    "Reason");
        Add("drawingdecide.reason.optional",   "(không bắt buộc khi Duyệt)",                                               "(optional on Approve)");

        Add("drawingdecide.cancel",            "Hủy",                                                                      "Cancel");

        Add("drawingdecide.title.reject",      "Từ chối chip {0} · v{1}",                                                  "Reject chip {0} · v{1}");
        Add("drawingdecide.title.approve",     "Duyệt chip {0} · v{1}",                                                    "Approve chip {0} · v{1}");

        Add("drawingdecide.submitting",        "Đang gửi…",                                                                "Submitting…");
        Add("drawingdecide.confirm.reject",    "Xác nhận Từ chối",                                                         "Confirm Reject");
        Add("drawingdecide.confirm.approve",   "Xác nhận Duyệt",                                                           "Confirm Approve");

        Add("drawingdecide.placeholder.reject", "Bắt buộc khi Từ chối — nêu rõ lý do để người thiết kế chỉnh sửa.",       "Required on Reject — state the reason clearly so the designer can revise.");
        Add("drawingdecide.placeholder.approve", "Có thể để trống.",                                                       "Can be left blank.");
    }
}
