namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/CopySpecModal.razor (copyspec.*).
public sealed partial class TranslationCatalog
{
    private void RegisterCopySpec()
    {
        //     key                          vi                                   en
        Add("copyspec.header",           "Sao chép Spec",                       "Copy Spec");
        Add("copyspec.load.failed",      "Không tải được danh sách sản phẩm.",  "Failed to load the product list.");
        Add("copyspec.retry",            "Thử lại",                             "Retry");
        Add("copyspec.loading",          "Đang tải danh sách sản phẩm…",        "Loading product list…");
        Add("copyspec.nosource",         "Chưa có Spec nguồn.",                 "No source Spec yet.");
        Add("copyspec.field.product",    "Sản phẩm đích",                       "Target product");
        Add("copyspec.product.select",   "— Chọn sản phẩm —",                   "— Select product —");
        Add("copyspec.field.speccode",   "Mã Spec mới",                         "New Spec Code");
        Add("copyspec.field.title",      "Tiêu đề mới",                         "New Title");
        Add("copyspec.cancel",           "Hủy",                                 "Cancel");
        Add("copyspec.copying",          "Đang sao chép…",                      "Copying…");
        Add("copyspec.copy",             "Sao chép",                            "Copy");
    }
}
