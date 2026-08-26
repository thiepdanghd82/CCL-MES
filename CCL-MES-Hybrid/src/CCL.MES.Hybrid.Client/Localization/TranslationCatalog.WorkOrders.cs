namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — WorkOrders.razor (wo.*).
public sealed partial class TranslationCatalog
{
    private void RegisterWorkOrders()
    {
        //     key                          vi                                            en
        Add("wo.pagetitle",              "Lệnh sản xuất — CCL MES",                     "Work Orders — CCL MES");
        Add("wo.heading",                "Lệnh sản xuất — Quét & Nhận",                 "Work Orders — Scan & Accept");

        // Scan-disabled gate.
        Add("wo.scandisabled.title",     "Chức năng quét đang tắt.",                    "Scanning is disabled.");
        Add("wo.scandisabled.body.pre",  "Đặt",                                         "Set");
        Add("wo.scandisabled.body.post", "trong appsettings.json (hoặc qua biến môi trường) để bật luồng quét → WO. Cấu hình tại Cài đặt → Thiết bị nếu trạm này đã được duyệt.",
                                          "in appsettings.json (or via env override) to enable the scan→WO flow. Configure it under Settings → Devices if this station has been approved.");

        // Camera / settings.
        Add("wo.camera.checking",        "Đang kiểm tra camera…",                       "Checking camera…");
        Add("wo.settings.opening",       "Đang mở…",                                    "Opening…");
        Add("wo.settings.open",          "Mở Cài đặt hệ thống",                         "Open System Settings");
        Add("wo.settings.openfailed",    "Không thể mở Cài đặt tự động. Vào Apple Menu → System Settings → Privacy & Security → Camera để cấp quyền cho CCL MES.",
                                          "Could not open Settings automatically. Go to Apple Menu → System Settings → Privacy & Security → Camera to grant permission for CCL MES.");

        // Scan / manual-entry CTA row.
        Add("wo.scan.opening",           "Đang mở camera…",                             "Opening camera…");
        Add("wo.scan.button",            "Quét WO",                                     "Scan WO");
        Add("wo.manual.placeholder",     "Nhập mã WO (vd WO-26-3683)",                  "Enter WO code (e.g. WO-26-3683)");
        Add("wo.manual.arialabel",       "Nhập mã WO thủ công",                         "Enter WO code manually");
        Add("wo.lookup.busy",            "Đang tra cứu…",                               "Looking up…");
        Add("wo.find",                   "Tìm",                                         "Find");
        Add("wo.scananother",            "Quét WO khác",                                "Scan another WO");
        Add("wo.lookup.progress",        "Đang tra cứu {0}…",                           "Looking up {0}…");

        // Active Work Orders landing list.
        Add("wo.active.head",            "Lệnh SX đang chạy ({0})",                     "Active Work Orders ({0})");
        Add("wo.active.empty",           "Không có lệnh SX đang chạy. Quét hoặc nhập mã WO ở trên để bắt đầu.",
                                          "No active work orders. Scan or type a WO code above to begin.");

        // Field labels (shared across card + sidebar).
        Add("wo.field.customer",         "Khách hàng",                                  "Customer");
        Add("wo.field.product",          "Sản phẩm",                                    "Product");
        Add("wo.field.productname",      "Tên sản phẩm",                                "Product name");
        Add("wo.field.productcode",      "Mã sản phẩm",                                 "Product code");
        Add("wo.field.partdesc",         "Mô tả chi tiết",                              "Part Desc");
        Add("wo.field.machine",          "Máy",                                         "Machine");
        Add("wo.field.target",           "Mục tiêu",                                    "Target");
        Add("wo.field.produced",         "Đã sản xuất",                                 "Produced");
        Add("wo.field.progress",         "Tiến độ",                                     "Progress");
        Add("wo.field.messtatus",        "Trạng thái MES",                              "MES status");
        Add("wo.field.plate",            "Bản in",                                      "Plate");
        Add("wo.field.cutter",           "Khuôn bế",                                    "Cutter");
        Add("wo.unit.pcs",               "cái",                                         "pcs");

        // WO card header actions.
        Add("wo.refresh",                "Làm mới",                                     "Refresh");
        Add("wo.closewo",                "Đóng WO",                                     "Close WO");

        // Advance banners + button.
        Add("wo.advance.success",        "Đã chuyển bước",                              "Step advanced");
        Add("wo.advance.failed",         "Không thể chuyển bước:",                      "Could not advance step:");
        Add("wo.advance.errorcode",      "Mã lỗi",                                      "Error code");
        Add("wo.networkerror",           "Lỗi mạng:",                                   "Network error:");
        Add("wo.advance.busy",           "Đang chuyển bước…",                           "Advancing step…");
        Add("wo.advance.button",         "Nhận / Bắt đầu (chuyển bước)",                "Accept / Start (advance step)");

        // Multi-leg fork.
        Add("wo.fork.button",            "Tách công đoạn (multi-leg)",                  "Split legs (multi-leg)");

        // Sidebar panels.
        Add("wo.side.currentstate",      "Trạng thái hiện tại",                         "Current State");
        Add("wo.side.phase",             "Công đoạn {0} / 7",                           "Phase {0} / 7");
        Add("wo.side.specquickref",      "Tra cứu Spec nhanh",                          "Spec Quick Ref");
        Add("wo.side.bomsummary",        "Tóm tắt BOM",                                 "BOM Summary");
        Add("wo.side.audittrail",        "Nhật ký truy vết",                            "Audit Trail");

        // L58 — nút ẩn/hiện cột tra cứu bên phải. Cột này khai cứng 320px; cộng
        // với rail trái 256px thì ở tablet 1024pt chrome chiếm ~49% màn hình.
        Add("wo.side.collapse",          "Ẩn cột tra cứu",                              "Hide reference column");
        Add("wo.side.expand",            "Hiện cột tra cứu",                            "Show reference column");
        Add("wo.bom.aside",              "{0} vật tư · {1} OK",                         "{0} materials · {1} OK");
        Add("wo.bom.lot",                "Lô {0}",                                      "Lot {0}");
        Add("wo.audit.events",           "{0} sự kiện",                                 "{0} events");
    }
}
