namespace CCL.MES.Hybrid.Client.Localization;

// Batch 2C — SettingDashboard.razor (setting.*).
public sealed partial class TranslationCatalog
{
    private void RegisterSetting()
    {
        //     key                              vi                                                      en
        Add("setting.loading",                 "Đang tải trạng thái SETTING…",                          "Loading SETTING status…");
        Add("setting.error.load",              "Không tải được trạng thái SETTING:",                    "Could not load SETTING status:");
        Add("setting.error.save",              "Không lưu được thay đổi:",                              "Could not save change:");
        Add("setting.dismiss",                 "Bỏ qua",                                                "Dismiss");

        Add("setting.invalidphase.title",      "Lệnh SX không ở bước SETTING",                          "WO is not in the SETTING phase");
        Add("setting.invalidphase.current",    "Hiện tại:",                                             "Current:");
        Add("setting.invalidphase.hint",       "Quay lại tab Lệnh SX để chọn lệnh khác.",               "Go back to the Work Orders tab to select another WO.");

        Add("setting.timer.label",             "Thời gian setting",                                     "Setting time");

        Add("setting.checklist.title",         "Danh mục kiểm tra setting",                             "Setting checklist");
        Add("setting.checklist.confirmed",     "Đã OK {0}/{1}",                                         "{0}/{1} OK");

        // ── Sub-tabs: quy trình In / quy trình Cắt (die-cut) ───────────────
        Add("setting.tab.print",               "Quy trình In",                                          "Print process");
        Add("setting.tab.cut",                 "Quy trình Cắt",                                         "Cut process");

        // ── Table column headers (cột "Xác nhận" tái dùng confirm.header) ──
        Add("setting.col.no",                  "#",                                                     "#");
        Add("setting.col.item",                "Hạng mục kiểm tra",                                     "Check item");
        Add("setting.col.standard",            "Tiêu chuẩn cần đạt",                                    "Required standard");

        // ── PRINT process makeready checklist ─────────────────────────────
        Add("setting.print.item0",  "Bản in / khuôn",                     "Print plate / die");
        Add("setting.print.std0",   "Đúng mã + phiên bản, đối chiếu artwork/spec",                 "Correct code + revision, matched to artwork/spec");
        Add("setting.print.item1",  "Vật tư in",                          "Print substrate");
        Add("setting.print.std1",   "Đúng loại/khổ/mặt xử lý (corona), đúng đơn hàng",             "Correct type/width/treatment (corona), matches the order");
        Add("setting.print.item2",  "Mực & màu",                          "Ink & colour");
        Add("setting.print.std2",   "Đúng mã pha, đối chiếu target Pantone/CxF; độ nhớt + pH đạt", "Correct mix code, matched to Pantone/CxF; viscosity + pH OK");
        Add("setting.print.item3",  "Chồng màu (registration)",           "Registration");
        Add("setting.print.std3",   "Lắp bản đúng vị trí, sai lệch ≤ dung sai spec",               "Plate mounted correctly, deviation ≤ spec tolerance");
        Add("setting.print.item4",  "Anilox / trục cấp mực",              "Anilox / ink feed");
        Add("setting.print.std4",   "Đúng line/BCM cho từng màu (flexo)",                          "Correct line/BCM per colour (flexo)");
        Add("setting.print.item5",  "Áp lực in + dao gạt mực",            "Impression + doctor blade");
        Add("setting.print.std5",   "Impression & doctor blade set đúng, không lem/thiếu nét",     "Impression & doctor blade set right, no smear/missing detail");
        Add("setting.print.item6",  "Sấy / UV curing",                    "Drying / UV curing");
        Add("setting.print.std6",   "Nhiệt/công suất đèn đúng thông số, mực khô-bám đạt",          "Lamp temp/power to spec, ink cured & adhered");
        Add("setting.print.item7",  "Mẫu đầu tiên",                       "First article");
        Add("setting.print.std7",   "Nội dung + chính tả + mã vạch (grade) + màu ΔE đạt spec",     "Content + spelling + barcode (grade) + colour ΔE meet spec");
        Add("setting.print.item8",  "Đăng ký mặt sau",                    "Back-side registration");
        Add("setting.print.std8",   "In 2 mặt: đăng ký mặt-sau trong dung sai (nếu có)",           "Duplex: back-side registration within tolerance (if any)");
        Add("setting.print.item9",  "Vệ sinh & an toàn",                  "Cleaning & safety");
        Add("setting.print.std9",   "Máy sạch, gọn vật tư thừa, an toàn khu vực",                  "Machine clean, scrap tidied, area safe");

        // ── CUT / die-cut process checklist ───────────────────────────────
        Add("setting.cut.item0",    "Khuôn cắt / dao",                    "Cutting die / knife");
        Add("setting.cut.std0",     "Đúng mã + phiên bản; lưỡi sắc, không mẻ",                     "Correct code + revision; sharp blade, no nicks");
        Add("setting.cut.item1",    "Lắp dao đúng khổ",                   "Mount correct-size die");
        Add("setting.cut.std1",     "Kiểu cắt đúng spec (kiss-cut / through-cut)",                 "Cut type per spec (kiss-cut / through-cut)");
        Add("setting.cut.item2",    "Nhấn / gấp (crease)",                "Crease / perf");
        Add("setting.cut.std2",     "Đúng vị trí & độ sâu (nếu có)",                               "Correct position & depth (if any)");
        Add("setting.cut.item3",    "Áp lực / độ sâu cắt",               "Cut pressure / depth");
        Add("setting.cut.std3",     "Kiss-cut không đứt liner; through-cut đứt đều",               "Kiss-cut doesn't sever liner; through-cut cuts cleanly");
        Add("setting.cut.item4",    "Canh cắt theo hình in",              "Die-to-print registration");
        Add("setting.cut.std4",     "Die-to-print registration ≤ dung sai",                        "Die-to-print registration ≤ tolerance");
        Add("setting.cut.item5",    "Layout con / tờ",                    "Up layout / sheet");
        Add("setting.cut.std5",     "Số con/hàng + gap/bước (pitch) đúng",                         "Ups per row + gap/pitch correct");
        Add("setting.cut.item6",    "Cuộn / lõi / tension",               "Rewind / core / tension");
        Add("setting.cut.std6",     "Hướng cuộn + lõi + sức căng đúng yêu cầu",                    "Rewind direction + core + web tension as required");
        Add("setting.cut.item7",    "Bóc lưới thải (matrix)",             "Matrix stripping");
        Add("setting.cut.std7",     "Trôi đều, không đứt/dính; biên cắt sạch, không rách/xơ",      "Strips evenly, no break/stick; clean cut edge, no tear/fray");
        Add("setting.cut.item8",    "Đếm số lượng",                       "Count");
        Add("setting.cut.std8",     "Số con/nhãn trên đơn vị đóng gói đúng",                        "Ups/labels per pack unit correct");
        Add("setting.cut.item9",    "Vệ sinh & an toàn",                  "Cleaning & safety");
        Add("setting.cut.std9",     "Máy sạch, thu gom phế, an toàn khu vực",                       "Machine clean, waste collected, area safe");

        Add("setting.done.button",             "Hoàn tất Setting (chuyển sang IPQC)",                   "Finish Setting (advance to IPQC)");
        Add("setting.action.hint",             "Xác nhận OK toàn bộ hạng mục In + Cắt → nút Hoàn tất sẽ bật.", "Confirm OK on all Print + Cut items → the Finish button activates.");
    }
}
