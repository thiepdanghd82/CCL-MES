namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — PrepressDashboard.razor (prepress.*).
public sealed partial class TranslationCatalog
{
    private void RegisterPrepress()
    {
        //     key                             vi                                                              en
        Add("prepress.loading",             "Đang tải danh mục kiểm PREPRESS…",                                "Loading PREPRESS checklist…");
        Add("prepress.initial_error",       "Không tải được danh mục kiểm PREPRESS:",                          "Could not load PREPRESS checklist:");

        Add("prepress.invalid_phase.title", "WO không ở công đoạn PREPRESS",                                   "WO is not in the PREPRESS phase");
        Add("prepress.invalid_phase.current", "Hiện tại:",                                                    "Current:");
        Add("prepress.invalid_phase.hint",  "Quay lại thẻ Lệnh SX để chọn WO khác.",                          "Go back to the Work Orders tab to select another WO.");

        Add("prepress.rollup.ready",        "Vật tư sẵn sàng ✓",                                              "Materials Ready ✓");
        Add("prepress.rollup.not_ready",    "Vật tư chưa sẵn sàng",                                           "Materials not ready");

        Add("prepress.set_error",           "Không lưu được thay đổi:",                                        "Could not save change:");
        Add("prepress.dismiss",             "Bỏ qua",                                                          "Dismiss");

        Add("prepress.reasons_empty.title", "Danh mục mã lỗi NG đang trống.",                                 "NG defect code catalog is empty.");
        Add("prepress.reasons_empty.body",  "Máy chủ chưa seed mã lý do (kind=Scrap). Báo IT — khởi động lại API để chạy boot probe",
                                            "The server has not seeded reason codes (kind=Scrap). Report to IT — restart the API to run the boot probe");
        Add("prepress.reasons_empty.hint",  "chỉ có thể ghi nhận OK cho tới khi khắc phục xong.",             "only OK can be recorded until this is fixed.");

        Add("prepress.scan.label",          "Quét vật tư",                                                     "Scan material");
        Add("prepress.scan.placeholder",    "Chĩa máy quét vào nhãn vật tư, hệ thống tự khớp…",               "Point the scanner at a material label, then it auto-matches…");
        Add("prepress.scan.count",          "{0}/{1} vật tư OK",                                              "{0}/{1} materials OK");

        Add("prepress.advance.button",      "Bắt đầu Cài đặt (chuyển bước)",                                   "Start Setting (advance step)");
        Add("prepress.advance.hint",        "Hoàn tất cả 3 danh mục kiểm (Vật tư + Bản in + Dao) → nút này sẽ được kích hoạt.",
                                            "Complete all 3 checklists (Material + Plate + Cutter) → this button becomes active.");
    }
}
