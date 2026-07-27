namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/QualityTraceability.razor (qualtrace.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQualTrace()
    {
        //     key                              vi                                                     en
        Add("qualtrace.page.title",          "Dữ liệu truy xuất — Chất lượng",                       "Traceability data — Quality");
        Add("qualtrace.heading",             "Dữ liệu truy xuất",                                    "Traceability data");

        Add("qualtrace.search.placeholder",  "Quét hoặc nhập Lệnh sản xuất…",                        "Scan or type Work Order…");
        Add("qualtrace.scanning",            "Đang quét…",                                           "Scanning…");
        Add("qualtrace.scan",                "Quét",                                                 "Scan");
        Add("qualtrace.search",              "Tìm kiếm",                                             "Search");
        Add("qualtrace.reset",               "Đặt lại cửa sổ",                                       "Reset windows");
        Add("qualtrace.reset.title",         "Đưa các cửa sổ về giữa màn hình",                      "Bring the windows back to the centre of the screen");

        Add("qualtrace.live.on",             "Trực tuyến",                                           "Live");
        Add("qualtrace.live.off",            "Ngoại tuyến",                                          "Offline");
        Add("qualtrace.live.on.title",       "Cập nhật thời gian thực đang bật",                     "Real-time updates on");
        Add("qualtrace.live.off.title",      "Ngoại tuyến — thăm dò mỗi 20 giây",                    "Offline — polling every 20s");

        Add("qualtrace.loading",             "Đang tải…",                                            "Loading…");
        Add("qualtrace.load.failed",         "Không tải được dữ liệu truy xuất.",                    "Failed to load traceability data.");
        Add("qualtrace.retry",               "Thử lại",                                              "Retry");

        Add("qualtrace.col.workorder",       "Lệnh sản xuất",                                        "Work Order");
        Add("qualtrace.col.product",         "Sản phẩm",                                             "Product");
        Add("qualtrace.col.mesphase",        "Công đoạn MES",                                        "MES phase");
        Add("qualtrace.col.frozenphases",    "Công đoạn đã đóng băng",                               "Frozen phases");
        Add("qualtrace.col.lastscan",        "Lần quét cuối",                                        "Last scan");

        Add("qualtrace.row.dblclick",        "Nhấp đúp để xem chi tiết đầy đủ",                      "Double-click for full detail");
        Add("qualtrace.empty",               "Chưa có lệnh sản xuất nào được quét.",                 "No work orders scanned yet.");

        Add("qualtrace.pager.previous",      "Trước",                                                "Previous");
        Add("qualtrace.pager.next",          "Sau",                                                  "Next");
        Add("qualtrace.pager.status",        "Trang {0} / {1} — {2} dòng",                           "Page {0} / {1} — {2} rows");

        Add("qualtrace.scan.cancelled",      "Đã hủy quét hoặc không có thiết bị.",                  "Scan cancelled or no hardware.");
        Add("qualtrace.scan.scanned",        "Đã quét: {0}",                                         "Scanned: {0}");
        Add("qualtrace.scan.error",          "Lỗi quét: {0}",                                        "Scan error: {0}");

        Add("qualtrace.error.connect",       "Không kết nối được tới máy chủ: {0}",                  "Could not connect to the server: {0}");
        Add("qualtrace.win.max",             "Đang mở tối đa {0} cửa sổ — đóng bớt để mở thêm.",     "At most {0} windows open — close some to open more.");
    }
}
