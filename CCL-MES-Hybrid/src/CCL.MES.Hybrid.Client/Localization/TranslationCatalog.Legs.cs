namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — LegsDashboard.razor (legs.*).
public sealed partial class TranslationCatalog
{
    private void RegisterLegs()
    {
        //     key                              vi                                                              en
        Add("legs.loading",                  "Đang tải công đoạn…",                                          "Loading legs…");
        Add("legs.load.failed",              "Không tải được công đoạn:",                                    "Could not load legs:");
        Add("legs.head.count",               "{0} công đoạn song song",                                      "{0} parallel legs");
        Add("legs.head.progress",            "{0}/{1} công đoạn xong",                                        "{0}/{1} legs done");

        // Nhãn CHẶNG của sơ đồ DAG (nhánh song song → hội tụ → kết thúc).
        Add("legs.stage.branch",             "Nhánh song song",                                              "Parallel branches");
        Add("legs.stage.converge",           "Hội tụ",                                                       "Converge");
        Add("legs.stage.terminal",           "Kết thúc → FQC",                                               "Final → FQC");

        // Per-leg readiness chips (this leg's OWN materialised check set).
        Add("legs.ready.materials",          "Vật tư {0}/{1}",                                               "Materials {0}/{1}");
        Add("legs.ready.ipqc",               "IPQC {0}/{1}",                                                 "IPQC {0}/{1}");

        // Per-leg IPQC drill-in toggle.
        Add("legs.ipqc.open",                "Kiểm IPQC công đoạn",                                          "Open leg IPQC");
        Add("legs.ipqc.close",               "Đóng",                                                         "Close");
        Add("legs.ipqc.modal.title",         "Kiểm tra IPQC",                                                "IPQC inspection");

        Add("legs.terminal",                 "⤳ cuối",                                                       "⤳ final");
        Add("legs.terminal.title",           "Công đoạn cuối — quyết định gộp FQC",                          "Final leg — decides the FQC join");

        Add("legs.gate.hard",                "⛔ Chờ bán thành phẩm (in/tape) xong mới chạy được.",          "⛔ Wait for the semi-finished stock (print/tape) before running.");
        Add("legs.gate.hard.title",          "Chưa chạy được",                                               "Can't run yet");
        Add("legs.gate.hard.hint",           "Hoàn tất nhánh IN + TAPE (đủ số lượng) rồi mới bắt đầu chạy.",  "Finish the PRINT + TAPE branches (full qty) before you can start running.");
        Add("legs.gate.soft",                "⏳ Công đoạn trước chưa xong (vẫn chạy song song được).",       "⏳ The previous leg is not finished yet (you can still run in parallel).");
        Add("legs.gate.soft.title",          "Chạy song song được",                                          "Can run in parallel");
        Add("legs.advance.blocked.tooltip",  "Chờ nhánh in/tape xong mới bắt đầu chạy được.",                "Finish the print/tape branches before you can start running.");

        Add("legs.done",                     "✓ Xong",                                                       "✓ Done");
        Add("legs.rework",                   "NG / làm lại",                                                 "NG / rework");

        Add("legs.semi.label",               "Kho bán thành phẩm",                                           "Semi-finished store");
        Add("legs.semi.reserve",             "Giữ lô (FEFO)",                                                "Reserve lot (FEFO)");
        Add("legs.semi.consume",             "Đã dùng xong",                                                 "Consumed");

        Add("legs.rework.title",             "NG công đoạn {0} — về PREPRESS",                               "NG leg {0} — back to PREPRESS");
        Add("legs.rework.reason.placeholder","Lý do NG / rework…",                                           "NG / rework reason…");
        Add("legs.cancel",                   "Huỷ",                                                          "Cancel");
        Add("legs.rework.confirm",           "Xác nhận NG",                                                  "Confirm NG");

        Add("legs.semi.modal.title",         "Giữ bán thành phẩm cho {0}",                                   "Reserve semi-finished stock for {0}");
        Add("legs.semi.kind",                "Loại",                                                         "Type");
        Add("legs.semi.kind.printed",        "In ({0})",                                                     "Print ({0})");
        Add("legs.semi.kind.tape",           "Tape ({0})",                                                   "Tape ({0})");
        Add("legs.semi.qty",                 "Số lượng",                                                     "Quantity");
        Add("legs.semi.lotno",               "Mã lô cụ thể (trống → FEFO tự chọn)",                          "Specific lot code (empty → FEFO auto-picks)");
        Add("legs.semi.lotno.placeholder",   "Quét lô hoặc để trống…",                                       "Scan a lot or leave empty…");
        Add("legs.semi.confirm",             "Giữ lô",                                                       "Reserve lot");

        Add("legs.banner.soft.running",      "Đã chạy — lưu ý công đoạn trước chưa xong (song song).",       "Running — note the previous leg is not finished yet (parallel).");

        Add("legs.advance.setting",          "Bắt đầu setup",                                                "Start setup");
        Add("legs.advance.ipqc_wait",        "Setup xong → IPQC",                                            "Setup done → IPQC");
        Add("legs.advance.ipqc_approved",    "IPQC duyệt",                                                   "IPQC approve");
        Add("legs.advance.running",          "Bắt đầu chạy",                                                 "Start running");
        Add("legs.advance.leg_done",         "Hoàn tất công đoạn",                                            "Complete leg");

        Add("legs.kind.print",               "IN",                                                           "PRINT");
        Add("legs.kind.cut",                 "CẮT",                                                          "CUT");
        Add("legs.kind.tape",                "CẮT TAPE",                                                     "TAPE CUT");
        Add("legs.kind.assembly",            "DÁN (assembly)",                                               "ASSEMBLY");
        Add("legs.kind.print_cut",           "IN + BẾ",                                                      "PRINT + DIE-CUT");

        Add("legs.phase.prepress",           "Chuẩn bị",                                                     "Prepress");
        Add("legs.phase.setting",            "Setup",                                                        "Setting");
        Add("legs.phase.ipqc_wait",          "Chờ IPQC",                                                     "Awaiting IPQC");
        Add("legs.phase.ipqc_approved",      "IPQC duyệt",                                                   "IPQC approved");
        Add("legs.phase.running",            "Đang chạy",                                                    "Running");
        Add("legs.phase.paused",             "Tạm dừng",                                                     "Paused");
        Add("legs.phase.leg_done",           "Xong",                                                         "Done");
    }
}
