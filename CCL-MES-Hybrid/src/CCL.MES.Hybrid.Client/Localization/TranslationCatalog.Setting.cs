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
        Add("setting.col.apply",               "Áp dụng",                                               "Applies");
        Add("setting.col.no",                  "#",                                                     "#");
        Add("setting.col.item",                "Hạng mục kiểm tra",                                     "Check item");
        Add("setting.col.standard",            "Tiêu chuẩn cần đạt",                                    "Required standard");
        Add("setting.col.result",              "Kết quả",                                               "Result");
        Add("setting.col.defect",              "Defect",                                                "Defect");
        Add("setting.applyall",                "Áp dụng tất cả",                                        "Apply to all");

        // ── Kết quả dẫn xuất (Confirm + Áp dụng) + defect picker khi NG ────
        Add("setting.result.pass",             "Đạt",                                                   "Pass");
        Add("setting.result.ng",               "NG",                                                    "NG");
        Add("setting.result.na",               "Không áp dụng",                                         "N/A");
        Add("setting.defect.select",           "— chọn defect —",                                       "— select defect —");

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

        // ── Defect per-hạng-mục (drop-list khi NG). Bộ tạm cho attestation-cục-bộ;
        //    7g sẽ rót từ CheckItemLibrary sau khi Ops xác nhận. ──────────────
        // PRINT
        Add("setting.defect.pl_ver",    "Sai phiên bản bản in",       "Wrong plate revision");
        Add("setting.defect.pl_wear",   "Mòn / xước bản",             "Worn / scratched plate");
        Add("setting.defect.pl_delam",  "Bong / hở hình",             "Delaminated / lifted image");
        Add("setting.defect.pl_sep",    "Sai tách màu",               "Wrong colour separation");
        Add("setting.defect.pl_dirt",   "Cấn / bẩn bản",              "Dented / dirty plate");
        Add("setting.defect.su_type",   "Sai loại / mã vật tư",       "Wrong substrate type / code");
        Add("setting.defect.su_size",   "Sai khổ",                    "Wrong width");
        Add("setting.defect.su_corona", "Sai mặt xử lý corona",       "Wrong corona-treated side");
        Add("setting.defect.su_damp",   "Ẩm / cong vênh",             "Damp / warped");
        Add("setting.defect.su_lot",    "Sai lô",                     "Wrong lot");
        Add("setting.defect.in_de",     "Lệch màu (ΔE)",              "Colour deviation (ΔE)");
        Add("setting.defect.in_code",   "Sai mã pha mực",             "Wrong ink mix code");
        Add("setting.defect.in_visc",   "Độ nhớt sai",               "Wrong viscosity");
        Add("setting.defect.in_ph",     "pH lệch",                    "pH out of range");
        Add("setting.defect.in_smear",  "Lem / loang mực",            "Smear / bleeding");
        Add("setting.defect.in_dry",    "Mực khô đầu",                "Ink drying on head");
        Add("setting.defect.rg_mis",    "Lệch chồng màu",             "Registration misalignment");
        Add("setting.defect.rg_edge",   "Lệch mép",                   "Edge misalignment");
        Add("setting.defect.rg_blur",   "Nhòe biên",                  "Blurred edge");
        Add("setting.defect.rg_dbl",    "Double image",               "Double image");
        Add("setting.defect.an_bcm",    "Sai line / BCM anilox",      "Wrong anilox line / BCM");
        Add("setting.defect.an_clog",   "Tắc ô anilox",               "Clogged anilox cells");
        Add("setting.defect.an_uneven", "Cấp mực không đều",          "Uneven ink feed");
        Add("setting.defect.an_blade",  "Dao gạt mòn",               "Worn doctor blade");
        Add("setting.defect.im_over",   "Quá áp lực in",              "Over-impression");
        Add("setting.defect.im_under",  "Thiếu áp lực in",            "Under-impression");
        Add("setting.defect.im_dline",  "Vệt dao gạt",               "Doctor blade line");
        Add("setting.defect.im_thin",   "Thiếu nét",                  "Missing detail");
        Add("setting.defect.cu_wet",    "Mực chưa khô",               "Ink not dry");
        Add("setting.defect.cu_over",   "Over-cure / giòn",           "Over-cured / brittle");
        Add("setting.defect.cu_adh",    "Bong mực (adhesion)",        "Poor ink adhesion");
        Add("setting.defect.cu_yellow", "Vàng nền",                   "Yellowing");
        Add("setting.defect.fo_content","Sai nội dung / chính tả",    "Wrong content / spelling");
        Add("setting.defect.fo_barcode","Barcode grade thấp",         "Low barcode grade");
        Add("setting.defect.fo_color",  "Sai màu",                    "Wrong colour");
        Add("setting.defect.fo_miss",   "Thiếu chi tiết",             "Missing detail");
        Add("setting.defect.br_mis",    "Lệch đăng ký 2 mặt",         "Duplex registration off");
        Add("setting.defect.br_dir",    "Sai hướng mặt sau",          "Wrong back-side direction");
        Add("setting.defect.br_show",   "Show-through (xuyên nền)",   "Show-through");
        Add("setting.defect.hk_dirty",  "Máy bẩn / dính mực",         "Dirty machine / residue");
        Add("setting.defect.hk_mess",   "Vật tư / phế bừa bộn",       "Untidy materials / waste");
        Add("setting.defect.hk_safety", "Thiếu an toàn khu vực",      "Area safety not met");
        // CUT
        Add("setting.defect.di_ver",    "Sai mã / phiên bản khuôn",   "Wrong die code / revision");
        Add("setting.defect.di_blunt",  "Lưỡi mẻ / cùn",              "Nicked / blunt blade");
        Add("setting.defect.di_break",  "Gãy dao",                    "Broken knife");
        Add("setting.defect.di_rust",   "Rỉ sét",                     "Rust");
        Add("setting.defect.ds_size",   "Sai khổ dao",                "Wrong die size");
        Add("setting.defect.ds_type",   "Sai kiểu cắt (kiss/through)","Wrong cut type (kiss/through)");
        Add("setting.defect.ds_mismount","Lắp dao lệch",              "Misaligned die mount");
        Add("setting.defect.cr_pos",    "Sai vị trí nhấn / gấp",      "Wrong crease position");
        Add("setting.defect.cr_depth",  "Sai độ sâu nhấn",            "Wrong crease depth");
        Add("setting.defect.cr_crack",  "Nứt gấp",                    "Cracked fold");
        Add("setting.defect.cr_miss",   "Thiếu crease",               "Missing crease");
        Add("setting.defect.cd_liner",  "Đứt liner",                  "Severed liner");
        Add("setting.defect.cd_nocut",  "Cắt không đứt",              "Incomplete cut");
        Add("setting.defect.cd_uneven", "Cắt không đều",              "Uneven cut");
        Add("setting.defect.cd_fray",   "Rìa xơ",                     "Frayed edge");
        Add("setting.defect.dr_mis",    "Lệch die-to-print",          "Die-to-print misalignment");
        Add("setting.defect.dr_crop",   "Xén vào hình in",            "Cut into print");
        Add("setting.defect.dr_edge",   "Lệch mép nhãn",              "Label edge off");
        Add("setting.defect.up_count",  "Sai số con / hàng",          "Wrong ups / rows");
        Add("setting.defect.up_pitch",  "Sai gap / bước (pitch)",     "Wrong gap / pitch");
        Add("setting.defect.up_overlap","Chồng con",                  "Overlapping ups");
        Add("setting.defect.up_miss",   "Thiếu con",                  "Missing ups");
        Add("setting.defect.rw_dir",    "Sai hướng cuộn",             "Wrong rewind direction");
        Add("setting.defect.rw_core",   "Sai lõi",                    "Wrong core");
        Add("setting.defect.rw_tension","Tension lỏng / chặt",        "Loose / tight tension");
        Add("setting.defect.rw_tele",   "Telescoping / nhăn",         "Telescoping / wrinkle");
        Add("setting.defect.mx_break",  "Đứt lưới thải",              "Matrix break");
        Add("setting.defect.mx_stick",  "Dính con",                   "Sticking labels");
        Add("setting.defect.mx_left",   "Sót thải",                   "Matrix residue left");
        Add("setting.defect.mx_tear",   "Rách biên khi bóc",          "Torn edge on stripping");
        Add("setting.defect.ct_wrong",  "Đếm sai",                    "Miscount");
        Add("setting.defect.ct_qty",    "Thiếu / thừa con",           "Under / over count");
        Add("setting.defect.ct_unit",   "Sai đơn vị đóng gói",        "Wrong pack unit");

        Add("setting.done.button",             "Hoàn tất Setting (chuyển sang IPQC)",                   "Finish Setting (advance to IPQC)");
        Add("setting.action.hint",             "Xác nhận OK toàn bộ hạng mục In + Cắt → nút Hoàn tất sẽ bật.", "Confirm OK on all Print + Cut items → the Finish button activates.");
    }
}
