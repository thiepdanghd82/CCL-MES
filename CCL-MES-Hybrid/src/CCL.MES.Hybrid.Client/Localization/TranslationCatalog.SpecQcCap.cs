namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2E — Shared/SpecQcCaptureTab.razor (specqccap.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSpecQcCap()
    {
        //     key                             vi                                                                              en
        Add("specqccap.empty",              "Chưa có kế hoạch QC nào để ghi nhận.",                                          "No QC plan to capture yet.");
        Add("specqccap.readonly.banner",    "Tài khoản hiện tại chỉ được xem kết quả QC. Cần quyền Admin hoặc Kỹ sư để Ghi nhận.", "The current account can only view QC results. Admin or Engineer is required to Record.");
        Add("specqccap.criteria.count",     "{0} tiêu chí",                                                                  "{0} criteria");

        Add("specqccap.col.criterion",      "Tiêu chí",                                                                      "Criterion");
        Add("specqccap.col.latest",         "Mới nhất",                                                                      "Latest");
        Add("specqccap.col.measurement",    "Số đo",                                                                         "Measurement");
        Add("specqccap.col.reason",         "Lý do",                                                                         "Reason");
        Add("specqccap.col.recordedby",     "Người ghi nhận",                                                                "Recorded by");
        Add("specqccap.col.time",           "Thời gian",                                                                     "Time");
        Add("specqccap.col.record",         "Ghi nhận",                                                                      "Record");

        Add("specqccap.target",             "Mục tiêu",                                                                      "Target");
        Add("specqccap.none",               "Chưa có",                                                                       "None");
        Add("specqccap.btn.record",         "+ Ghi nhận",                                                                    "+ Record");
        Add("specqccap.history.summary",    "Xem lịch sử ({0} bản ghi)",                                                     "View history ({0} records)");

        Add("specqccap.hist.result",        "Kết quả",                                                                       "Result");
        Add("specqccap.hist.by",            "Bởi",                                                                           "By");
        Add("specqccap.hist.comment",       "Ghi chú",                                                                       "Comment");
    }
}
