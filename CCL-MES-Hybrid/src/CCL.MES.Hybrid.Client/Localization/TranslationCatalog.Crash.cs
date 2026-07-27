namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/RendererCrashBoundary.razor (crash.*).
public sealed partial class TranslationCatalog
{
    private void RegisterCrash()
    {
        //     key               vi                                                                                                   en
        Add("crash.title",   "Có lỗi không xác định",                                                                                "An unexpected error occurred");
        Add("crash.body",    "Một thành phần giao diện vừa gặp lỗi. Bạn có thể tải lại trang để tiếp tục — dữ liệu chưa lưu có thể mất.", "A UI component has crashed. You can reload the page to continue — any unsaved data may be lost.");
        Add("crash.details", "Chi tiết lỗi",                                                                                         "Error details");
        Add("crash.reload",  "Tải lại",                                                                                              "Reload");
    }
}
