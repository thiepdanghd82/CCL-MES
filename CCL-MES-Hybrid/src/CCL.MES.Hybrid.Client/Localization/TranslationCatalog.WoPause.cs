namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — WoPauseModal.razor (wopause.*).
public sealed partial class TranslationCatalog
{
    private void RegisterWoPause()
    {
        //     key                          vi                                                     en
        Add("wopause.header",            "Tạm dừng (PAUSE) — chọn lý do",                        "Pause (PAUSE) — select a reason");
        Add("wopause.empty.title",       "Danh mục lý do tạm dừng đang trống.",                  "PAUSE reason catalog is empty.");
        Add("wopause.empty.body",        "Máy chủ chưa nạp mã lý do (kind=Pause). Báo cho IT.",  "The server has not seeded reason codes (kind=Pause). Report to IT.");
        Add("wopause.reason.label",      "Lý do tạm dừng *",                                     "Pause reason *");
        Add("wopause.reason.placeholder","— chọn một lý do —",                                   "— select a reason —");
        Add("wopause.note.label",        "Ghi chú (tuỳ chọn, ≤ 500 ký tự)",                      "Note (optional, ≤ 500 characters)");
        Add("wopause.cancel",            "Huỷ",                                                  "Cancel");
        Add("wopause.confirm",           "Tạm dừng",                                             "Pause");
    }
}
