namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/MachineDashboard.razor (machine.*).
public sealed partial class TranslationCatalog
{
    private void RegisterMachine()
    {
        //     key                             vi                                                        en
        Add("machine.page.title",           "Bảng điều khiển máy — CCL MES",                            "Machine Dashboard — CCL MES");
        Add("machine.head.title",           "Giám sát máy · Xưởng sản xuất",                            "Machine Monitoring · Plant Floor");
        Add("machine.head.subtitle",        "Trạng thái trực tiếp trên mọi trung tâm sản xuất · nhóm theo khu vực", "Live status across all work centers · grouped by area");

        Add("machine.view.mode",            "Chế độ xem",                                               "View mode");
        Add("machine.view.grid",            "Dạng lưới",                                                "Grid");
        Add("machine.view.list",            "Dạng danh sách",                                           "List");
        Add("machine.loading.short",        "Đang tải…",                                                "Loading…");
        Add("machine.refresh",              "↻ Làm mới",                                                "↻ Refresh");

        Add("machine.kpi.plantquality",     "Chất lượng xưởng",                                         "Plant Quality");
        Add("machine.kpi.output.summary",   "{0} sản phẩm đã SX · {1} đang chạy",                        "{0} pcs produced · {1} active");

        Add("machine.loading.machines",     "Đang tải danh sách máy…",                                  "Loading machines…");
        Add("machine.error.load",           "Không tải được danh sách máy: {0}",                        "Failed to load machines: {0}");
        Add("machine.empty.workcenters",    "Không có trung tâm sản xuất.",                             "No work centers.");
        Add("machine.empty.filter",         "Không có máy nào khớp bộ lọc.",                            "No machines match the filter.");

        Add("machine.search.placeholder",   "🔍 Tìm mã WC, lệnh SX, sản phẩm, khách hàng…",             "🔍 Search WC code, WO, product, customer…");
        Add("machine.filter.count",         "{0} / {1} máy",                                            "{0} / {1} machines");
        Add("machine.filter.allareas",      "Tất cả khu vực",                                           "All areas");

        Add("machine.col.workcenter",       "Trung tâm SX",                                             "Work center");
        Add("machine.col.area",             "Khu vực",                                                  "Area");
        Add("machine.col.status",           "Trạng thái",                                               "Status");
        Add("machine.col.wo",               "Lệnh SX",                                                  "WO");
        Add("machine.col.customer",         "Khách hàng",                                               "Customer");
        Add("machine.col.product",          "Sản phẩm",                                                 "Product");
        Add("machine.col.progress",         "Tiến độ",                                                  "Progress");
        Add("machine.col.good",             "Đạt",                                                      "Good");
        Add("machine.col.reject",           "Loại",                                                     "Reject");
        Add("machine.col.quality",          "Chất lượng",                                               "Quality");
        Add("machine.col.updated",          "Cập nhật",                                                 "Updated");

        Add("machine.list.idle",            "— nhàn rỗi —",                                             "— idle —");

        Add("machine.area.count",           "{0} máy",                                                  "{0} machines");
        Add("machine.area.quality",         "Chất lượng {0}",                                           "Quality {0}");

        Add("machine.stat.good",            "Đạt",                                                      "Good");
        Add("machine.stat.reject",          "Loại",                                                     "Reject");
        Add("machine.stat.produced",        "Đã sản xuất",                                              "Produced");
        Add("machine.stat.yield",           "Hiệu suất {0}%",                                           "Yield {0}%");
        Add("machine.stat.defect",          "{0}% lỗi",                                                 "{0}% defect");
        Add("machine.card.quality",         "Chất lượng",                                               "Quality");
        Add("machine.card.idle.waiting",    "◯ Nhàn rỗi · chờ lệnh SX",                                 "◯ Idle · waiting for WO");
        Add("machine.card.idle.none",       "Không có lệnh sản xuất đang chạy trên máy này.",           "No active work order on this machine.");

        Add("machine.side.recent",          "Hoạt động gần đây",                                        "Recent Activity");
        Add("machine.side.recent.empty",    "Không có máy nào đang chạy.",                              "No active machines.");
        Add("machine.side.attention",       "Cần chú ý",                                                "Needs Attention");
        Add("machine.side.attention.empty", "Không có cảnh báo chất lượng.",                            "No quality alerts.");
        Add("machine.side.alert",           "{0}% lỗi trên {1}",                                        "{0}% defect on {1}");

        Add("machine.drawer.title",         "Chi tiết máy",                                             "Machine detail");
        Add("machine.drawer.loading",       "Đang tải chi tiết…",                                       "Loading detail…");
        Add("machine.drawer.nodata",        "Không có dữ liệu máy.",                                    "No machine data.");
        Add("machine.drawer.description",   "Mô tả",                                                    "Description");
        Add("machine.drawer.area",          "Khu vực",                                                  "Area");
        Add("machine.drawer.status",        "Trạng thái",                                               "Status");
        Add("machine.drawer.activewo",      "Lệnh SX đang chạy",                                        "Active WO");
        Add("machine.drawer.progress",      "Tiến độ",                                                  "Progress");
        Add("machine.drawer.noactivewo",    "Không có lệnh SX đang chạy.",                              "No active WO.");
        Add("machine.drawer.today",         "Hôm nay",                                                  "Today");
        Add("machine.drawer.today.wo",      "Lệnh SX",                                                  "WO");
        Add("machine.drawer.today.good",    "Đạt",                                                      "Good");
        Add("machine.drawer.today.ng",      "NG",                                                       "NG");
        Add("machine.drawer.recentwos",     "Lệnh SX gần đây",                                          "Recent WOs");
        Add("machine.drawer.nowos",         "Không có lệnh SX.",                                        "No WOs.");
        Add("machine.drawer.col.wo",        "Lệnh SX",                                                  "WO");
        Add("machine.drawer.col.phase",     "Công đoạn",                                                "Phase");
        Add("machine.drawer.col.progress",  "Tiến độ",                                                  "Progress");
    }
}
