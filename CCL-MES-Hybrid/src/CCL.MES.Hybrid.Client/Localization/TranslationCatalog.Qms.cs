namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/QmsQueue.razor (qms.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQms()
    {
        //     key                    vi                                        en
        Add("qms.pagetitle",       "Hàng chờ kiểm",                           "Inspection Queue");
        Add("qms.title",           "Hàng chờ kiểm (QMS)",                     "Inspection Queue (QMS)");
        Add("qms.refresh",         "Làm mới",                                 "Refresh");
        Add("qms.loading",         "Đang tải…",                               "Loading…");
        Add("qms.loading.queue",   "Đang tải hàng chờ…",                      "Loading queue…");
        Add("qms.load.failed",     "Không tải được hàng chờ: {0}",            "Failed to load queue: {0}");
        Add("qms.empty",           "Không có lệnh SX nào đang chờ {0}.",       "No WOs waiting for {0}.");

        Add("qms.col.product",     "Sản phẩm",                                "Product");
        Add("qms.col.machine",     "Máy",                                     "Machine");
        Add("qms.col.plandone",    "Kế hoạch/Đã làm",                         "Plan/Done");
        Add("qms.col.waitingsince","Chờ từ",                                  "Waiting since");
    }
}
