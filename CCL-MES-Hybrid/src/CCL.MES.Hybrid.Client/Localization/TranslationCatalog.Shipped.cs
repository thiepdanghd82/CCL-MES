namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — ShippedSummaryDashboard.razor (shipped.*).
public sealed partial class TranslationCatalog
{
    private void RegisterShipped()
    {
        //     key                              vi                                  en
        Add("shipped.loading",              "Đang tải tổng kết lệnh SX…",        "Loading WO summary…");
        Add("shipped.load.failed",          "Không tải được tổng kết:",           "Could not load summary:");
        Add("shipped.shippedat",            "Xuất xưởng lúc",                    "Shipped at");

        Add("shipped.output",               "Sản lượng",                         "Output");
        Add("shipped.target",               "Mục tiêu",                          "Target");
        Add("shipped.done",                 "Đã làm",                            "Done");
        Add("shipped.ngpct",                "NG %",                              "NG %");

        Add("shipped.time",                 "Thời gian",                         "Time");
        Add("shipped.run",                  "Chạy",                              "Run");
        Add("shipped.pause",                "Tạm dừng",                          "Pause");
        Add("shipped.sessions",             "Số phiên",                          "Sessions");

        Add("shipped.availability",         "Khả dụng",                          "Availability");
        Add("shipped.performance",          "Hiệu suất",                         "Performance");
        Add("shipped.quality",              "Chất lượng",                        "Quality");
        Add("shipped.oee.total",            "OEE tổng",                          "OEE total");

        // Đợt 1 C3 — why Performance has no number. The API returns a reason
        // code instead of a silent null; these render it. Keys mirror
        // OeePerformance.Reason* one-for-one.
        Add("shipped.perf.na.workcenter_missing",
            "Lệnh chưa gán máy, hoặc mã máy không khớp công đoạn nào",
            "No machine assigned, or the code matches no work center");
        Add("shipped.perf.na.workcenter_speed_missing",
            "Công đoạn chưa khai tốc độ lý tưởng (pcs/h)",
            "Work center has no ideal speed set (pcs/h)");
        Add("shipped.perf.na.no_runtime",
            "Lệnh chưa chạy nên chưa có thời gian để tính",
            "The WO has not run yet, so there is no runtime to divide by");
        Add("shipped.perf.na.unknown",
            "Không tính được hiệu suất",
            "Performance could not be computed");

        Add("shipped.pareto.title",         "Pareto tạm dừng (top-5)",           "Pause Pareto (top-5)");
        Add("shipped.qc.summary",           "Tổng kết QC",                       "QC summary");

        Add("shipped.sig.inspector",        "Người kiểm",                        "Inspector");
        Add("shipped.sig.reviewer",         "Người soát",                        "Reviewer");
        Add("shipped.sig.approver",         "Người duyệt",                       "Approver");

        Add("shipped.judgment.pass",        "Đạt",                             "Pass");
        Add("shipped.judgment.pending",     "— Chờ",                             "— Pending");
        Add("shipped.judgment.reject",      "Loại",                            "Reject");
        Add("shipped.judgment.gorun",       "Cho chạy",                        "Go Run");
        Add("shipped.judgment.stopline",    "Dừng chuyền",                     "Stop Line");
        Add("shipped.judgment.specialaccept", "Chấp nhận đặc biệt",            "Special Accept");
    }
}
