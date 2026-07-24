namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2F — Shared/QcCaptureModal.razor (qccapture.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQcCapture()
    {
        //     key                                        vi                                              en
        Add("qccapture.title",                        "Ghi nhận QC · v{0} — {1}",                     "Capture QC · v{0} — {1}");
        Add("qccapture.title.unnamed",                "(chưa đặt tên)",                               "(unnamed)");

        Add("qccapture.help.record_for",              "Ghi nhận kết quả cho tiêu chí",                "Record the result for criterion");
        Add("qccapture.help.stage",                   "Công đoạn",                                    "Stage");

        Add("qccapture.field.result",                 "Kết quả",                                      "Result");
        Add("qccapture.field.measurement",            "Số đo (tùy chọn)",                             "Measurement (optional)");
        Add("qccapture.field.measurement.placeholder","vd 12.5mm / OK / ±0.1",                        "e.g. 12.5mm / OK / ±0.1");
        Add("qccapture.field.reason",                 "Mã lý do",                                     "Reason code");
        Add("qccapture.field.reason.required_on_fail","(bắt buộc khi FAIL)",                          "(required on FAIL)");
        Add("qccapture.field.reason.select",          "— Chọn mã lý do —",                            "— Select reason code —");
        Add("qccapture.field.comment",                "Ghi chú",                                      "Comment");

        Add("qccapture.result.pass",                  "✓ ĐẠT",                                        "✓ PASS");
        Add("qccapture.result.fail",                  "✗ KHÔNG ĐẠT",                                  "✗ FAIL");
        Add("qccapture.result.na",                    "— N/A",                                        "— N/A");

        Add("qccapture.action.cancel",                "Hủy",                                          "Cancel");
        Add("qccapture.action.recording",             "Đang ghi…",                                    "Recording…");
        Add("qccapture.action.record_pass",           "Ghi nhận ĐẠT",                                 "Record PASS");
        Add("qccapture.action.record_fail",           "Ghi nhận KHÔNG ĐẠT",                           "Record FAIL");
        Add("qccapture.action.record_na",             "Ghi nhận N/A",                                 "Record N/A");
    }
}
