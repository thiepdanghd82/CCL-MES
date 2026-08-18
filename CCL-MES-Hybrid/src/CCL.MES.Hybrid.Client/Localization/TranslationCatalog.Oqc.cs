namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — OqcDashboard.razor (oqc.*).
public sealed partial class TranslationCatalog
{
    private void RegisterOqc()
    {
        //     key                                    vi                                                              en
        Add("oqc.loading",                        "Đang tải trạng thái OQC…",                                     "Loading OQC status…");
        Add("oqc.load.error",                     "Không tải được trạng thái OQC:",                               "Could not load OQC status:");
        Add("oqc.invalid.phase.title",            "Lệnh SX không ở giai đoạn OQC_PENDING",                        "WO is not in the OQC_PENDING phase");
        Add("oqc.invalid.phase.current",          "Hiện tại:",                                                    "Current:");
        Add("oqc.invalid.phase.hint",             "Quay lại tab Lệnh SX để chọn lệnh khác.",                     "Go back to the Work Orders tab to select another WO.");

        Add("oqc.counter.label",                  "Hạng mục · Chữ ký",                                            "Items · Sigs");

        Add("oqc.sig.policy.label",               "Chính sách 3 chữ ký:",                                       "3-sig policy:");

        Add("oqc.save.error",                     "Không lưu được thay đổi:",                                     "Could not save change:");
        Add("oqc.dismiss",                        "Bỏ qua",                                                       "Dismiss");

        Add("oqc.sig.chain.title",                "Chuỗi 3 chữ ký OQC",                                           "OQC 3-sig chain");
        Add("oqc.sig.inspector",                  "1 · Người kiểm",                                               "1 · Inspector");
        Add("oqc.sig.reviewer",                   "2 · Người soát",                                               "2 · Reviewer");
        Add("oqc.sig.approver",                   "3 · Người duyệt",                                              "3 · Approver");
        Add("oqc.pending",                        "— Chờ",                                                        "— Pending");

        Add("oqc.empty.profile.title",            "Hồ sơ OQC đang trống.",                                        "OQC profile is empty.");
        Add("oqc.empty.profile.hint",             "Quản trị viên phải khởi tạo hồ sơ OQC trước khi vận hành. Báo IT để cấu hình.", "An administrator must seed the OQC profile before operation. Report to IT to configure.");

        Add("oqc.btn.ok",                         "OK",                                                           "OK");
        Add("oqc.btn.ng",                         "NG",                                                           "NG");

        Add("oqc.ng.code.label",                  "Mã NG:",                                                       "NG code:");
        Add("oqc.ng.code.field",                  "Mã NG",                                                        "NG code");
        Add("oqc.ng.code.select",                 "— chọn mã NG —",                                               "— select NG code —");
        Add("oqc.ng.note.field",                  "Ghi chú NG (1-500 ký tự)",                                     "NG note (1-500 characters)");
        Add("oqc.cancel",                         "Huỷ",                                                          "Cancel");
        Add("oqc.ng.save",                        "Lưu NG",                                                       "Save NG");

        Add("oqc.status.ok",                      "OK",                                                         "OK");
        Add("oqc.status.ng",                      "NG",                                                         "NG");
        Add("oqc.status.pending",                 "— Chờ",                                                        "— Pending");

        Add("oqc.stage.title.inspecting",         "Đang kiểm các hạng mục OQC",                                   "Inspecting OQC items");
        Add("oqc.stage.title.inspector",          "1 · Người kiểm ký",                                            "1 · Inspector sign");
        Add("oqc.stage.title.reviewer",           "2 · Người soát ký",                                            "2 · Reviewer sign");
        Add("oqc.stage.title.approver",           "3 · Người duyệt quyết định",                                   "3 · Approver decision");

        Add("oqc.rollup.anyng",                   "Có hạng mục NG — Người kiểm ký để chuyển tiếp; Người duyệt sẽ Từ chối về FQC.", "One or more items NG — Inspector signs to advance; the Approver will Reject back to FQC.");

        Add("oqc.stage.pending.hint",             "Còn {0} hạng mục chưa xác nhận. Hoàn tất, rồi nhấn",           "{0} item(s) not yet confirmed. Complete them, then tap");
        Add("oqc.stage.pending.hint.action",      "Người kiểm ký",                                                "Inspector sign");

        Add("oqc.btn.inspector.sign",             "1 · Người kiểm ký",                                            "1 · Inspector sign");
        Add("oqc.inspector.hint",                 "Tôi xác nhận đã kiểm {0} hạng mục theo hồ sơ OQC.",           "I confirm I have inspected {0} items per the OQC profile.");

        Add("oqc.btn.reviewer.sign",              "2 · Người soát ký",                                            "2 · Reviewer sign");
        Add("oqc.reviewer.differ.title",          "Người soát phải khác Người kiểm",                              "Reviewer must differ from Inspector");
        Add("oqc.reviewer.hint",                  "Người soát xác nhận kết quả Người kiểm đã ký",                 "The Reviewer confirms the result the Inspector signed");

        Add("oqc.reject.reason.field",            "Lý do từ chối (bắt buộc khi từ chối; 1-500 ký tự)",           "Reject reason (required when rejecting; 1-500 characters)");
        Add("oqc.approver.differ.inspector.title","Người duyệt phải khác Người kiểm",                             "Approver must differ from Inspector");
        Add("oqc.approver.differ.reviewer.title", "Người duyệt phải khác Người soát",                             "Approver must differ from Reviewer");
        Add("oqc.btn.approve",                    "Duyệt → SHIPPED",                                              "Approve → SHIPPED");
        Add("oqc.btn.reject",                     "Từ chối → FQC",                                                "Reject → FQC");
        Add("oqc.approver.hint",                  "Người duyệt ra quyết định cuối cùng. Người kiểm =",           "The Approver makes the final decision. Inspector =");
        Add("oqc.approver.hint.reviewer",         "Người soát =",                                                 "Reviewer =");

        Add("oqc.error.connect",                  "Không kết nối được máy chủ: {0}",                              "Could not connect to the server: {0}");
        Add("oqc.error.loading",                  "Lỗi khi tải trạng thái OQC: {0}",                              "Error loading OQC status: {0}");
        Add("oqc.error.no.code",                  "Máy chủ trả về Ok=false mà không có mã lỗi — báo IT.",         "Server returned Ok=false without an error code — report to IT.");
    }
}
