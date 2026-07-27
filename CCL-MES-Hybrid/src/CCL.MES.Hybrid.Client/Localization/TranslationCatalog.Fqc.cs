namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — FqcDashboard.razor (fqc.*).
public sealed partial class TranslationCatalog
{
    private void RegisterFqc()
    {
        //     key                          vi                                                              en
        Add("fqc.loading",               "Đang tải trạng thái FQC…",                                     "Loading FQC status…");

        Add("fqc.initial.error",         "Không tải được trạng thái FQC:",                               "Could not load FQC status:");
        Add("fqc.error.connect",         "Không kết nối được máy chủ",                                   "Could not connect to the server");
        Add("fqc.error.loading",         "Lỗi khi tải trạng thái FQC",                                   "Error loading FQC status");
        Add("fqc.error.no.code",         "Máy chủ trả về Ok=false mà không có mã lỗi — báo IT.",         "Server returned Ok=false without an error code — report to IT.");

        Add("fqc.invalid.phase.title",   "WO không ở công đoạn FQC_PENDING",                             "WO is not in the FQC_PENDING phase");
        Add("fqc.invalid.phase.current", "Hiện tại",                                                     "Current");
        Add("fqc.invalid.phase.hint",    "Quay lại tab Lệnh SX để chọn WO khác.",                        "Go back to the Work Orders tab to select another WO.");

        Add("fqc.counter.label",         "Số mục đã xác nhận",                                           "Items confirmed");

        Add("fqc.set.error",             "Không lưu được thay đổi:",                                     "Could not save change:");
        Add("fqc.dismiss",               "Bỏ qua",                                                       "Dismiss");

        Add("fqc.empty.title",           "Hồ sơ QC đang trống.",                                         "QC profile is empty.");
        Add("fqc.empty.body",            "Quản trị viên phải nạp hồ sơ FQC (28 mục {0}) trước khi vận hành. Báo IT để cấu hình.",
                                         "An administrator must seed the FQC profile (28-item {0}) before operation. Report to IT to configure.");

        Add("fqc.btn.ok",                "OK",                                                           "OK");
        Add("fqc.btn.ng",                "NG",                                                           "NG");

        Add("fqc.ng.code.label",         "Mã NG:",                                                       "NG code:");
        Add("fqc.ng.code",               "Mã NG",                                                        "NG code");
        Add("fqc.ng.code.select",        "— chọn mã NG —",                                               "— select NG code —");
        Add("fqc.ng.note.label",         "Ghi chú NG (1-500 ký tự)",                                     "NG note (1-500 characters)");
        Add("fqc.cancel",                "Huỷ",                                                          "Cancel");
        Add("fqc.ng.save",               "Lưu NG",                                                       "Save NG");

        Add("fqc.judgment.title",        "Phán định FQC (Nhân viên kiểm)",                               "FQC judgment (Inspector)");
        Add("fqc.rollup.allok",          "Tất cả mục OK — nên chọn Đạt",                                 "All items OK — Pass recommended");
        Add("fqc.rollup.anyng",          "Có mục NG — chọn Từ chối (quay lại {0})",                      "One or more items NG — choose Reject (back to {0})");
        Add("fqc.rollup.pending",        "Còn {0} mục chưa xác nhận",                                    "{0} item(s) not yet confirmed");

        Add("fqc.judgment.pass",         "Đạt → {0}",                                                    "Pass → {0}");
        Add("fqc.judgment.reject",       "Từ chối → {0}",                                                "Reject → {0}");

        Add("fqc.reject.reason.label",   "Lý do từ chối (bắt buộc; 1-500 ký tự)",                        "Reject reason (required; 1-500 characters)");

        Add("fqc.hint.confirm",          "Xác nhận tất cả các mục FQC → các nút phán định sẽ được kích hoạt.",
                                         "Confirm all FQC items → the judgment buttons become active.");
        Add("fqc.hint.allok",            "Tất cả mục OK — chỉ Đạt là hợp lệ.",                           "All items OK — only Pass is valid.");
        Add("fqc.hint.anyng",            "Có mục NG — Từ chối sẽ trả WO về {0} để làm lại.",             "One or more items NG — Reject sends the WO back to {0} for rework.");

        Add("fqc.status.ok",             "✓ OK",                                                         "✓ OK");
        Add("fqc.status.ng",             "✗ NG",                                                         "✗ NG");
        Add("fqc.status.pending",        "— Chờ",                                                        "— Pending");
    }
}
