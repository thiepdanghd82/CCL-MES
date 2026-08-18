namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — QaApprovalDashboard.razor (qa.*).
public sealed partial class TranslationCatalog
{
    private void RegisterQaApproval()
    {
        //     key                          vi                                                              en
        Add("qa.loading",                  "Đang tải trạng thái QA…",                                       "Loading QA status…");
        Add("qa.load.error",               "Không tải được trạng thái QA:",                                 "Could not load QA status:");
        Add("qa.invalid.phase",            "WO không ở giai đoạn QA_PENDING",                               "WO is not in the QA_PENDING phase");
        Add("qa.invalid.phase.current",    "Hiện tại:",                                                     "Current:");
        Add("qa.invalid.phase.hint",       "Quay lại tab Lệnh SX để chọn WO khác.",                        "Go back to the Work Orders tab to select another WO.");

        Add("qa.submitted.by",             "IPQC nộp bởi",                                                  "IPQC submitted by");

        Add("qa.save.error",               "Không lưu được thay đổi:",                                      "Could not save change:");
        Add("qa.dismiss",                  "Bỏ qua",                                                        "Dismiss");

        Add("qa.dualsig.policy",           "Chính sách chữ ký kép:",                                     "Dual-sig policy:");

        Add("qa.ipqc.summary",             "Tóm tắt IPQC (chỉ đọc)",                                        "IPQC summary (read-only)");
        Add("qa.rollup.allok",             "Tất cả OK",                                                     "All OK");
        Add("qa.rollup.someng",            "Một hoặc nhiều hạng mục NG",                                    "One or more slots NG");

        Add("qa.slot.material",            "Vật tư",                                                        "Material");
        Add("qa.slot.printa",              "In A (Màu/ΔE)",                                                 "Print A (Color/ΔE)");
        Add("qa.slot.printb",              "In B (Chồng màu)",                                              "Print B (Registration)");
        Add("qa.slot.printc",              "In C (Nội dung)",                                               "Print C (Content)");

        Add("qa.special.reason.label",     "Lý do chấp nhận đặc biệt (do IPQC nhập)",                       "Special Accept reason (entered by IPQC)");

        Add("qa.judgment.title",           "Phán quyết QA",                                                 "QA judgment");
        Add("qa.note.label",               "Ghi chú QA (tuỳ chọn khi Duyệt · BẮT BUỘC khi Từ chối; 1-500 ký tự)", "QA note (optional for Approve · REQUIRED for Reject; 1-500 characters)");

        Add("qa.approve",                  "Duyệt",                                                         "Approve");
        Add("qa.reject",                   "Từ chối",                                                       "Reject");
        Add("qa.approve.tooltip",          "Người duyệt QA phải khác người nộp IPQC",                       "The QA approver must differ from the IPQC submitter");

        Add("qa.hint.q3",                  "Nút Duyệt bị khoá vì bạn chính là người nộp IPQC. Đăng xuất và đăng nhập bằng tài khoản QC khác để duyệt.", "The Approve button is locked because you are the IPQC submitter. Log out and log in with a different QC account to approve.");
        Add("qa.hint.no.submitter",        "Chưa có thông tin người nộp IPQC — tải lại trang.",            "No IPQC submitter information yet — reload the page.");
    }
}
