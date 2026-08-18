namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Pages/ShopOrderHistory.razor (shoporders.*).
public sealed partial class TranslationCatalog
{
    private void RegisterShopOrders()
    {
        //     key                              vi                                                     en
        Add("shoporders.pagetitle",          "Lịch sử lệnh sản xuất — CCL MES",                       "Shop Order History — CCL MES");
        Add("shoporders.title",              "Lịch sử lệnh sản xuất",                                 "Shop Order History");
        Add("shoporders.subtitle",           "Hồ sơ truy vết lệnh SX đã hoàn tất · tổng {0}",         "Forensic record of completed work orders · {0} total");
        Add("shoporders.export.csv",         "Xuất CSV",                                              "Export CSV");

        // KPI tiles
        Add("shoporders.kpi.totalwos",       "Tổng lệnh SX",                                          "Total Work Orders");
        Add("shoporders.kpi.totalwos.sub",   "{0} dừng · {1} xong",                                   "{0} stopped · {1} done");
        Add("shoporders.kpi.output",         "Tổng sản lượng",                                        "Total Output");
        Add("shoporders.kpi.output.sub",     "cái trên {0} lệnh SX",                                  "pcs across {0} WOs");
        Add("shoporders.kpi.yield",          "Hiệu suất TB",                                          "Avg Yield");
        Add("shoporders.kpi.yield.sub",      "mục tiêu ≥ 98%",                                        "target ≥ 98%");
        Add("shoporders.kpi.oee",            "OEE TB",                                                "Avg OEE");
        Add("shoporders.kpi.oee.sub",        "A × P × Q trên tất cả lệnh SX",                         "A × P × Q across all WOs");
        Add("shoporders.kpi.reject",         "Tổng phế",                                              "Total Reject");
        Add("shoporders.kpi.reject.sub",     "tỷ lệ lỗi {0}%",                                        "{0}% defect rate");

        // Search + filters
        Add("shoporders.search.placeholder", "Tìm lệnh SX / khách hàng / sản phẩm / máy…",        "Search WO / customer / product / machine…");
        Add("shoporders.records",            "{0} bản ghi",                                           "{0} records");
        Add("shoporders.filter.period",      "Kỳ:",                                                   "Period:");
        Add("shoporders.filter.status",      "Trạng thái:",                                           "Status:");
        Add("shoporders.filter.customer",    "Khách hàng:",                                           "Customer:");
        Add("shoporders.filter.machine",     "Máy:",                                                  "Machine:");
        Add("shoporders.filter.allcustomers","Tất cả khách hàng",                                     "All customers");
        Add("shoporders.filter.allmachines", "Tất cả máy",                                            "All machines");

        // Period chips
        Add("shoporders.period.all",         "Tất cả thời gian",                                      "All time");
        Add("shoporders.period.today",       "Hôm nay",                                               "Today");
        Add("shoporders.period.7d",          "7 ngày qua",                                            "Last 7 days");
        Add("shoporders.period.30d",         "30 ngày qua",                                           "Last 30 days");

        // Status chips + status labels
        Add("shoporders.statuschip.all",     "Tất cả",                                                "All");
        Add("shoporders.status.done",        "Xong",                                                "Done");
        Add("shoporders.status.stopped",     "Dừng",                                                "Stopped");

        // Empty / loading / error states
        Add("shoporders.loading",            "Đang tải lịch sử…",                                     "Loading history…");
        Add("shoporders.error.load",         "Không tải được lịch sử: {0}",                           "Failed to load history: {0}");
        Add("shoporders.empty",              "Không có lệnh SX nào khớp bộ lọc.",                     "No shop orders match the filter.");

        // Table headers
        Add("shoporders.th.wocustomer",      "Lệnh SX / Khách hàng",                                  "WO / Customer");
        Add("shoporders.th.product",         "Sản phẩm",                                              "Product");
        Add("shoporders.th.machine",         "Máy",                                                   "Machine");
        Add("shoporders.th.outputplan",      "Sản lượng / Kế hoạch",                                  "Output / Plan");
        Add("shoporders.th.yield",           "Hiệu suất",                                             "Yield");
        Add("shoporders.th.oee",             "OEE",                                                   "OEE");
        Add("shoporders.th.runtime",         "Thời gian chạy",                                        "Runtime");
        Add("shoporders.th.pauses",          "Số lần dừng",                                           "Pauses");
        Add("shoporders.th.finished",        "Hoàn tất",                                              "Finished");
        Add("shoporders.th.status",          "Trạng thái",                                            "Status");

        // Table cells
        Add("shoporders.cell.ng",            "{0} phế",                                               "{0} NG");
        Add("shoporders.cell.setup",         "cài đặt {0}",                                           "setup {0}");
        Add("shoporders.cell.events",        "lần",                                                   "events");

        // Sidebar
        Add("shoporders.side.topcustomers",  "Khách hàng hàng đầu",                                   "Top Customers");
        Add("shoporders.side.topmachines",   "Máy hàng đầu",                                          "Top Machines");
        Add("shoporders.side.topdowntime",   "Lý do dừng máy nhiều nhất",                             "Top Downtime Reasons");
        Add("shoporders.side.nopauses",      "Chưa ghi nhận lần dừng nào.",                           "No pauses recorded.");
        Add("shoporders.side.nodata",        "Không có dữ liệu.",                                     "No data.");
        Add("shoporders.side.tip",           "Mẹo",                                                   "Tip");
        Add("shoporders.tip.row",            "Nhấn một dòng → ngăn chi tiết",                         "Click a row → detail drawer");
        Add("shoporders.tip.export",         "Xuất CSV để dựng pivot table trong Excel",             "Export CSV for Excel pivot tables");
        Add("shoporders.tip.filter",         "Lọc ngày + máy → truy vết một sự cố",                  "Filter date + machine → trace one incident");

        // Detail drawer
        Add("shoporders.drawer.finalsummary","Tổng kết cuối",                                         "Final Summary");
        Add("shoporders.drawer.status",      "Trạng thái",                                            "Status");
        Add("shoporders.drawer.finished",    "Hoàn tất",                                              "Finished");
        Add("shoporders.drawer.targetqty",   "Số lượng kế hoạch",                                     "Target qty");
        Add("shoporders.drawer.actualoutput","Sản lượng thực tế",                                     "Actual output");
        Add("shoporders.drawer.reject",      "Phế",                                                   "Reject");
        Add("shoporders.drawer.perfmetrics", "Chỉ số hiệu suất",                                      "Performance Metrics");
        Add("shoporders.drawer.yield",       "Hiệu suất",                                             "Yield");
        Add("shoporders.drawer.oee",         "OEE",                                                   "OEE");
        Add("shoporders.drawer.oee.none",    "— (không có dữ liệu chạy/chu kỳ)",                     "— (no run/cycle data)");
        Add("shoporders.drawer.setuptime",   "Thời gian cài đặt",                                     "Setup time");
        Add("shoporders.drawer.totalruntime","Tổng thời gian chạy",                                   "Total runtime");
        Add("shoporders.drawer.pauses",      "Số lần dừng",                                           "Pauses");
        Add("shoporders.drawer.downtimeevents","Sự kiện dừng máy ({0})",                              "Downtime Events ({0})");
        Add("shoporders.drawer.eventcount",  "{0}× lần",                                              "{0}× events");
    }
}
