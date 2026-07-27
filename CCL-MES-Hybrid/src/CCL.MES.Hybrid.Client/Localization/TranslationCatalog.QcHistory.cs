namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Pages/QcHistory.razor (qchistory.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQcHistory()
    {
        //     key                              vi                                              en
        Add("qchistory.pagetitle",              "Lịch sử QC — CCL MES",                         "QC History — CCL MES");
        Add("qchistory.title",                  "Lịch sử QC",                                   "QC History");
        Add("qchistory.subtitle",               "Hồ sơ kiểm tra FQC / OQC đã hoàn tất · {0} lượt kiểm", "Forensic record of completed FQC / OQC checks · {0} checks");
        Add("qchistory.export.csv",             "Xuất CSV",                                     "Export CSV");

        // KPI tiles
        Add("qchistory.kpi.total",              "Tổng lượt kiểm",                               "Total Checks");
        Add("qchistory.kpi.pass",               "Đạt",                                          "Pass");
        Add("qchistory.kpi.pct.ofchecks",       "{0}% số lượt kiểm",                            "{0}% of checks");
        Add("qchistory.kpi.reject",             "Loại",                                         "Reject");
        Add("qchistory.kpi.passrate",           "Tỷ lệ đạt",                                    "Pass Rate");
        Add("qchistory.kpi.target",             "mục tiêu ≥ 98%",                               "target ≥ 98%");
        Add("qchistory.kpi.distinctwos",        "Số LSX riêng",                                 "Distinct WOs");
        Add("qchistory.kpi.distinctwos.sub",    "lệnh sản xuất đã kiểm",                        "work orders inspected");

        // Filters
        Add("qchistory.search.placeholder",     "🔍 Tìm LSX…",                                  "🔍 Search WO…");
        Add("qchistory.filter.records",         "{0} bản ghi",                                  "{0} records");
        Add("qchistory.filter.kind",            "Loại kiểm:",                                   "Kind:");
        Add("qchistory.filter.result",          "Kết quả:",                                     "Result:");
        Add("qchistory.chip.all",               "Tất cả",                                       "All");

        // States
        Add("qchistory.loading",                "Đang tải lịch sử…",                            "Loading history…");
        Add("qchistory.error.load",             "Không tải được lịch sử: {0}",                  "Failed to load history: {0}");
        Add("qchistory.empty",                  "Không có lượt kiểm QC nào khớp bộ lọc.",       "No QC checks match the filter.");

        // Table columns
        Add("qchistory.col.wo",                 "LSX",                                          "WO");
        Add("qchistory.col.kind",               "Loại",                                         "Kind");
        Add("qchistory.col.result",             "Kết quả",                                      "Result");
        Add("qchistory.col.inspector",          "Người kiểm",                                   "Inspector");
        Add("qchistory.col.reviewer",           "Người soát",                                   "Reviewer");
        Add("qchistory.col.approver",           "Người duyệt",                                  "Approver");
        Add("qchistory.col.finished",           "Hoàn tất",                                     "Finished");

        // Result badges
        Add("qchistory.badge.pass",             "✓ Đạt",                                        "✓ Pass");
        Add("qchistory.badge.reject",           "✕ Loại",                                       "✕ Reject");

        // Sidebar
        Add("qchistory.side.bykind",            "Theo loại",                                    "By Kind");
        Add("qchistory.side.topinspectors",     "Người kiểm hàng đầu",                          "Top Inspectors");
        Add("qchistory.side.topreject",         "Lý do loại hàng đầu",                          "Top Reject Reasons");
        Add("qchistory.side.norejects",         "Không có lượt loại.",                          "No rejects.");
        Add("qchistory.side.nodata",            "Không có dữ liệu.",                            "No data.");
        Add("qchistory.side.noreason",          "(không có lý do)",                             "(no reason)");

        // Detail drawer
        Add("qchistory.drawer.sub",             "Lượt kiểm {0}",                                "{0} check");
        Add("qchistory.drawer.result",          "Kết quả",                                      "Result");
        Add("qchistory.drawer.judgment",        "Phán định",                                    "Judgment");
        Add("qchistory.drawer.kind",            "Loại",                                         "Kind");
        Add("qchistory.drawer.finished",        "Hoàn tất",                                     "Finished");
        Add("qchistory.drawer.reason",          "Lý do",                                        "Reason");
        Add("qchistory.drawer.signatures",      "Chữ ký",                                       "Signatures");
        Add("qchistory.drawer.inspector",       "Người kiểm",                                   "Inspector");
        Add("qchistory.drawer.reviewer",        "Người soát",                                   "Reviewer");
        Add("qchistory.drawer.approver",        "Người duyệt",                                  "Approver");

        // Relative time
        Add("qchistory.time.justnow",           "vừa xong",                                     "just now");
        Add("qchistory.time.hours",             "{0} giờ trước",                                "{0}h ago");
        Add("qchistory.time.days",              "{0} ngày trước",                               "{0}d ago");
    }
}
