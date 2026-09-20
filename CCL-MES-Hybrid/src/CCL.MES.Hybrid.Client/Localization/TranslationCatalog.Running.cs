namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — RunningDashboard.razor (running.*).
public sealed partial class TranslationCatalog
{
    private void RegisterRunning()
    {
        //     key                                     vi                                                            en
        Add("running.loading",                     "Đang tải trạng thái chạy…",                                   "Loading run status…");
        Add("running.initial.error",               "Không tải được trạng thái RUNNING:",                          "Could not load RUNNING status:");

        Add("running.done.recorded",               "Đã ghi nhận Done:",                                           "Done recorded:");
        Add("running.ng.recorded",                 "Đã ghi nhận NG:",                                             "NG recorded:");

        Add("running.invalid.phase",               "WO không ở giai đoạn đang chạy",                              "WO is not in a running phase");
        Add("running.invalid.current",             "Hiện tại:",                                                   "Current:");
        Add("running.invalid.goback",              "Quay lại tab Lệnh SX để chọn WO khác.",                       "Go back to the Work Orders tab to select another WO.");

        Add("running.paused.label",                "Tạm dừng",                                                    "Paused");
        Add("running.set.error",                   "Không lưu được thay đổi:",                                    "Could not save change:");
        Add("running.dismiss",                     "Bỏ qua",                                                      "Dismiss");

        Add("running.ipqc.approved.title",         "IPQC đã duyệt — bắt đầu chạy",                                "IPQC approved — start running");

        Add("running.material.remediation.title", "Khắc phục vật tư đang chặn máy chạy",
                                                 "Fix the material blocking the run");
        Add("running.start",                       "Bắt đầu chạy",                                                "Start running");

        Add("running.counter.done",                "Đạt",                                                         "Done");
        Add("running.counter.ng",                  "NG",                                                          "NG");
        Add("running.counter.target",              "Mục tiêu",                                                    "Target");

        Add("running.addoutput.title",             "Thêm sản lượng (Đạt)",                                        "Add output (Done)");
        Add("running.tap.paused.hint",             "Đang tạm dừng — nhấn \"Tiếp tục chạy\"để bật lại các nút.",  "Paused — tap \"Resume running\"to re-enable the buttons.");

        Add("running.ng.title",                    "Đánh NG (Loại)",                                              "Reject NG (Reject)");
        Add("running.ng.quantity",                 "Số lượng NG",                                                 "NG quantity");
        Add("running.ng.defectcode",               "Mã lỗi NG *",                                                 "NG defect code *");
        Add("running.ng.defectcode.placeholder",   "— chọn mã lỗi —",                                            "— select defect code —");
        Add("running.ng.note",                     "Ghi chú * (1-500 ký tự)",                                     "Note * (1-500 characters)");
        Add("running.ng.submit",                   "Ghi nhận NG",                                                 "Record NG");

        Add("running.scrap.empty.strong",          "Danh mục NG đang trống.",                                     "NG catalog is empty.");
        Add("running.scrap.empty.body",            "Máy chủ chưa seed mã lý do (kind=Scrap). Báo cho IT.",        "The server has not seeded reason codes (kind=Scrap). Report to IT.");

        Add("running.pause",                       "Tạm dừng",                                                    "Pause");
        Add("running.resume",                      "Tiếp tục chạy",                                               "Resume running");
        Add("running.correct",                     "Sửa số",                                                      "Correct qty");
        Add("running.finish",                      "Kết thúc WO",                                                 "Finish WO");

        Add("running.deferred.done.title",         "WO đã hoàn tất",                                              "WO completed");
        Add("running.deferred.done.body",          "WO đã đóng. Không còn thao tác sản xuất nào trên WO này.",    "The WO is closed. No further production actions are available on this WO.");
        Add("running.deferred.cancelled.title",    "WO đã huỷ",                                                   "WO cancelled");
        Add("running.deferred.cancelled.body",     "WO đã bị huỷ. Không thể chạy hoặc ghi nhận thêm sản lượng.",  "The WO has been cancelled. It cannot be run or accept further output.");
        Add("running.deferred.scan.hint",          "Quét WO khác để bắt đầu công việc mới.",                      "Scan another WO to start new work.");

        // Đồng hồ chạy máy (OEE). "Chạy máy" = tổng các phiên chạy, ĐÃ TRỪ
        // thời gian tạm dừng — pause đóng phiên nên hai con số rời nhau.
        Add("running.timer.run.label",             "Thời gian chạy máy",                                          "Machine run time");
        Add("running.timer.pause.label",           "Đã tạm dừng",                                                 "Paused");
        Add("running.timer.availability.label",    "Khả dụng",                                                    "Availability");
        Add("running.timer.paused.now",            "Đang tạm dừng",                                               "Paused now");
        Add("running.timer.hint",                  "Tổng các phiên chạy, không tính lúc tạm dừng.",               "Total of all run sessions, excluding paused time.");
    }
}
