namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7g — seed data cho khâu SETTING (makeready). 20 hạng mục thư viện
/// (10 Print "SET-PR-00".."09" + 10 Cut "SET-CU-00".."09") + ~75 defect
/// option base (ProductCode null = dùng chung mọi mã hàng).
///
/// <para>Đây là MASTER DATA thuần (không I/O). DbSeeder upsert idempotent
/// non-deleting theo natural key. Nhãn VI + EN đi cùng (parity bắt buộc);
/// mã defect (<see cref="DefectRow.DefectCode"/>) trùng key i18n
/// <c>setting.defect.&lt;code&gt;</c> để client render nhãn không cần map.</para>
///
/// <para>Ánh xạ hạng mục → defect codes lấy từ SettingDashboard
/// (<c>_printItems</c>/<c>_cutItems</c> mảng DefectKeys) + doc
/// <c>setting-ng-reason-codes.md</c>. Chuẩn hạng mục lấy từ
/// <c>docs/setting-library-seed.csv</c>.</para>
/// </summary>
public static class SettingLibrarySeed
{
    /// <summary>Một hạng mục SETTING (map 1:1 sang CheckItemLibrary khi seed,
    /// stage Setting=true, ProductCode null).</summary>
    public sealed record ItemRow(
        string ItemId, string ProcessKind, int Sort, string GroupLabel,
        string ItemVi, string ItemEn, string AcceptanceVi, string AcceptanceEn);

    /// <summary>Một tuỳ chọn defect base (map sang CheckItemDefectOption,
    /// ProductCode null). <see cref="ItemId"/> = hạng mục sở hữu.</summary>
    public sealed record DefectRow(
        string ItemId, string DefectCode, string LabelVi, string LabelEn, int Sort);

    // ── 20 hạng mục (10 Print + 10 Cut) — đồng bộ setting-library-seed.csv ──

    public static IReadOnlyList<ItemRow> Items() => _items;

    private static readonly ItemRow[] _items =
    {
        // PRINT
        new("SET-PR-00", "Print", 0, "Khuôn & vật tư", "Bản in / khuôn", "Print plate / die",
            "Đúng mã + phiên bản đối chiếu artwork/spec", "Correct code + revision matched to artwork/spec"),
        new("SET-PR-01", "Print", 1, "Khuôn & vật tư", "Vật tư in", "Print substrate",
            "Đúng loại/khổ/mặt xử lý (corona) đúng đơn hàng", "Correct type/width/treatment matches order"),
        new("SET-PR-02", "Print", 2, "Khuôn & vật tư", "Mực & màu", "Ink & colour",
            "Đúng mã pha đối chiếu Pantone/CxF; độ nhớt + pH đạt", "Correct mix code matched to Pantone/CxF; viscosity + pH OK"),
        new("SET-PR-03", "Print", 3, "Cân chỉnh máy", "Chồng màu (registration)", "Registration",
            "Lắp bản đúng vị trí sai lệch ≤ dung sai spec", "Plate mounted correctly deviation within tolerance"),
        new("SET-PR-04", "Print", 4, "Cân chỉnh máy", "Anilox / trục cấp mực", "Anilox / ink feed",
            "Đúng line/BCM cho từng màu (flexo)", "Correct line/BCM per colour (flexo)"),
        new("SET-PR-05", "Print", 5, "Cân chỉnh máy", "Áp lực in + dao gạt mực", "Impression + doctor blade",
            "Impression & doctor blade set đúng không lem/thiếu nét", "Impression & doctor blade set right no smear"),
        new("SET-PR-06", "Print", 6, "Cân chỉnh máy", "Sấy / UV curing", "Drying / UV curing",
            "Nhiệt/công suất đèn đúng thông số mực khô-bám đạt", "Lamp temp/power to spec ink cured & adhered"),
        new("SET-PR-07", "Print", 7, "Mẫu đầu & an toàn", "Mẫu đầu tiên", "First article",
            "Nội dung + chính tả + mã vạch (grade) + màu ΔE đạt spec", "Content + spelling + barcode grade + colour ΔE meet spec"),
        new("SET-PR-08", "Print", 8, "Mẫu đầu & an toàn", "Đăng ký mặt sau", "Back-side registration",
            "In 2 mặt: đăng ký mặt-sau trong dung sai (nếu có)", "Duplex back-side registration within tolerance"),
        new("SET-PR-09", "Print", 9, "Mẫu đầu & an toàn", "Vệ sinh & an toàn", "Cleaning & safety",
            "Máy sạch gọn vật tư thừa an toàn khu vực", "Machine clean scrap tidied area safe"),
        // CUT
        new("SET-CU-00", "Cut", 0, "Dao & khuôn", "Khuôn cắt / dao", "Cutting die / knife",
            "Đúng mã + phiên bản; lưỡi sắc không mẻ", "Correct code + revision; sharp blade no nicks"),
        new("SET-CU-01", "Cut", 1, "Dao & khuôn", "Lắp dao đúng khổ", "Mount correct-size die",
            "Kiểu cắt đúng spec (kiss-cut / through-cut)", "Cut type per spec (kiss-cut / through-cut)"),
        new("SET-CU-02", "Cut", 2, "Dao & khuôn", "Nhấn / gấp (crease)", "Crease / perf",
            "Đúng vị trí & độ sâu (nếu có)", "Correct position & depth (if any)"),
        new("SET-CU-03", "Cut", 3, "Cân chỉnh", "Áp lực / độ sâu cắt", "Cut pressure / depth",
            "Kiss-cut không đứt liner; through-cut đứt đều", "Kiss-cut does not sever liner; through-cut cuts cleanly"),
        new("SET-CU-04", "Cut", 4, "Cân chỉnh", "Canh cắt theo hình in", "Die-to-print registration",
            "Die-to-print registration ≤ dung sai", "Die-to-print registration within tolerance"),
        new("SET-CU-05", "Cut", 5, "Cân chỉnh", "Layout con / tờ", "Up layout / sheet",
            "Số con/hàng + gap/bước (pitch) đúng", "Ups per row + gap/pitch correct"),
        new("SET-CU-06", "Cut", 6, "Cân chỉnh", "Cuộn / lõi / tension", "Rewind / core / tension",
            "Hướng cuộn + lõi + sức căng đúng yêu cầu", "Rewind direction + core + web tension as required"),
        new("SET-CU-07", "Cut", 7, "Mẫu đầu & thải", "Bóc lưới thải (matrix)", "Matrix stripping",
            "Trôi đều không đứt/dính; biên cắt sạch không rách/xơ", "Strips evenly no break/stick; clean cut edge"),
        new("SET-CU-08", "Cut", 8, "Mẫu đầu & thải", "Đếm số lượng", "Count",
            "Số con/nhãn trên đơn vị đóng gói đúng", "Ups/labels per pack unit correct"),
        new("SET-CU-09", "Cut", 9, "Mẫu đầu & thải", "Vệ sinh & an toàn", "Cleaning & safety",
            "Máy sạch thu gom phế an toàn khu vực", "Machine clean waste collected area safe"),
    };

    // ── ~75 defect option base (ProductCode null). Nhãn khớp i18n
    //    setting.defect.<code> (VI/EN parity) — code = key suffix. ──────────

    public static IReadOnlyList<DefectRow> Defects() => _defects.Value;

    // Lazy so it never runs at static-init time before _label is populated
    // (static field init order footgun). Thread-safe single build.
    private static readonly Lazy<DefectRow[]> _defects = new(BuildDefects);

    // Ánh xạ item → defect codes (mirror SettingDashboard _printItems/_cutItems).
    private static DefectRow[] BuildDefects()
    {
        (string ItemId, string[] Codes)[] map =
        {
            // PRINT
            ("SET-PR-00", new[] { "pl_ver", "pl_wear", "pl_delam", "pl_sep", "pl_dirt" }),
            ("SET-PR-01", new[] { "su_type", "su_size", "su_corona", "su_damp", "su_lot" }),
            ("SET-PR-02", new[] { "in_de", "in_code", "in_visc", "in_ph", "in_smear", "in_dry" }),
            ("SET-PR-03", new[] { "rg_mis", "rg_edge", "rg_blur", "rg_dbl" }),
            ("SET-PR-04", new[] { "an_bcm", "an_clog", "an_uneven", "an_blade" }),
            ("SET-PR-05", new[] { "im_over", "im_under", "im_dline", "im_thin" }),
            ("SET-PR-06", new[] { "cu_wet", "cu_over", "cu_adh", "cu_yellow" }),
            ("SET-PR-07", new[] { "fo_content", "fo_barcode", "fo_color", "fo_miss" }),
            ("SET-PR-08", new[] { "br_mis", "br_dir", "br_show" }),
            ("SET-PR-09", new[] { "hk_dirty", "hk_mess", "hk_safety" }),
            // CUT
            ("SET-CU-00", new[] { "di_ver", "di_blunt", "di_break", "di_rust" }),
            ("SET-CU-01", new[] { "ds_size", "ds_type", "ds_mismount" }),
            ("SET-CU-02", new[] { "cr_pos", "cr_depth", "cr_crack", "cr_miss" }),
            ("SET-CU-03", new[] { "cd_liner", "cd_nocut", "cd_uneven", "cd_fray" }),
            ("SET-CU-04", new[] { "dr_mis", "dr_crop", "dr_edge" }),
            ("SET-CU-05", new[] { "up_count", "up_pitch", "up_overlap", "up_miss" }),
            ("SET-CU-06", new[] { "rw_dir", "rw_core", "rw_tension", "rw_tele" }),
            ("SET-CU-07", new[] { "mx_break", "mx_stick", "mx_left", "mx_tear" }),
            ("SET-CU-08", new[] { "ct_wrong", "ct_qty", "ct_unit" }),
            ("SET-CU-09", new[] { "hk_dirty", "hk_mess", "hk_safety" }),
        };

        var rows = new List<DefectRow>();
        foreach (var (itemId, codes) in map)
        {
            var sort = 0;
            foreach (var code in codes)
            {
                var (vi, en) = _label[code];
                rows.Add(new DefectRow(itemId, code, vi, en, sort += 10));
            }
        }
        return rows.ToArray();
    }

    // Nhãn VI/EN mỗi defect code — parity với TranslationCatalog.Setting.cs.
    private static readonly Dictionary<string, (string Vi, string En)> _label = new(StringComparer.Ordinal)
    {
        // PRINT
        ["pl_ver"]   = ("Sai phiên bản bản in", "Wrong plate revision"),
        ["pl_wear"]  = ("Mòn / xước bản", "Worn / scratched plate"),
        ["pl_delam"] = ("Bong / hở hình", "Delaminated / lifted image"),
        ["pl_sep"]   = ("Sai tách màu", "Wrong colour separation"),
        ["pl_dirt"]  = ("Cấn / bẩn bản", "Dented / dirty plate"),
        ["su_type"]  = ("Sai loại / mã vật tư", "Wrong substrate type / code"),
        ["su_size"]  = ("Sai khổ", "Wrong width"),
        ["su_corona"]= ("Sai mặt xử lý corona", "Wrong corona-treated side"),
        ["su_damp"]  = ("Ẩm / cong vênh", "Damp / warped"),
        ["su_lot"]   = ("Sai lô", "Wrong lot"),
        ["in_de"]    = ("Lệch màu (ΔE)", "Colour deviation (ΔE)"),
        ["in_code"]  = ("Sai mã pha mực", "Wrong ink mix code"),
        ["in_visc"]  = ("Độ nhớt sai", "Wrong viscosity"),
        ["in_ph"]    = ("pH lệch", "pH out of range"),
        ["in_smear"] = ("Lem / loang mực", "Smear / bleeding"),
        ["in_dry"]   = ("Mực khô đầu", "Ink drying on head"),
        ["rg_mis"]   = ("Lệch chồng màu", "Registration misalignment"),
        ["rg_edge"]  = ("Lệch mép", "Edge misalignment"),
        ["rg_blur"]  = ("Nhòe biên", "Blurred edge"),
        ["rg_dbl"]   = ("Double image", "Double image"),
        ["an_bcm"]   = ("Sai line / BCM anilox", "Wrong anilox line / BCM"),
        ["an_clog"]  = ("Tắc ô anilox", "Clogged anilox cells"),
        ["an_uneven"]= ("Cấp mực không đều", "Uneven ink feed"),
        ["an_blade"] = ("Dao gạt mòn", "Worn doctor blade"),
        ["im_over"]  = ("Quá áp lực in", "Over-impression"),
        ["im_under"] = ("Thiếu áp lực in", "Under-impression"),
        ["im_dline"] = ("Vệt dao gạt", "Doctor blade line"),
        ["im_thin"]  = ("Thiếu nét", "Missing detail"),
        ["cu_wet"]   = ("Mực chưa khô", "Ink not dry"),
        ["cu_over"]  = ("Over-cure / giòn", "Over-cured / brittle"),
        ["cu_adh"]   = ("Bong mực (adhesion)", "Poor ink adhesion"),
        ["cu_yellow"]= ("Vàng nền", "Yellowing"),
        ["fo_content"]= ("Sai nội dung / chính tả", "Wrong content / spelling"),
        ["fo_barcode"]= ("Barcode grade thấp", "Low barcode grade"),
        ["fo_color"] = ("Sai màu", "Wrong colour"),
        ["fo_miss"]  = ("Thiếu chi tiết", "Missing detail"),
        ["br_mis"]   = ("Lệch đăng ký 2 mặt", "Duplex registration off"),
        ["br_dir"]   = ("Sai hướng mặt sau", "Wrong back-side direction"),
        ["br_show"]  = ("Show-through (xuyên nền)", "Show-through"),
        ["hk_dirty"] = ("Máy bẩn / dính mực", "Dirty machine / residue"),
        ["hk_mess"]  = ("Vật tư / phế bừa bộn", "Untidy materials / waste"),
        ["hk_safety"]= ("Thiếu an toàn khu vực", "Area safety not met"),
        // CUT
        ["di_ver"]   = ("Sai mã / phiên bản khuôn", "Wrong die code / revision"),
        ["di_blunt"] = ("Lưỡi mẻ / cùn", "Nicked / blunt blade"),
        ["di_break"] = ("Gãy dao", "Broken knife"),
        ["di_rust"]  = ("Rỉ sét", "Rust"),
        ["ds_size"]  = ("Sai khổ dao", "Wrong die size"),
        ["ds_type"]  = ("Sai kiểu cắt (kiss/through)", "Wrong cut type (kiss/through)"),
        ["ds_mismount"]= ("Lắp dao lệch", "Misaligned die mount"),
        ["cr_pos"]   = ("Sai vị trí nhấn / gấp", "Wrong crease position"),
        ["cr_depth"] = ("Sai độ sâu nhấn", "Wrong crease depth"),
        ["cr_crack"] = ("Nứt gấp", "Cracked fold"),
        ["cr_miss"]  = ("Thiếu crease", "Missing crease"),
        ["cd_liner"] = ("Đứt liner", "Severed liner"),
        ["cd_nocut"] = ("Cắt không đứt", "Incomplete cut"),
        ["cd_uneven"]= ("Cắt không đều", "Uneven cut"),
        ["cd_fray"]  = ("Rìa xơ", "Frayed edge"),
        ["dr_mis"]   = ("Lệch die-to-print", "Die-to-print misalignment"),
        ["dr_crop"]  = ("Xén vào hình in", "Cut into print"),
        ["dr_edge"]  = ("Lệch mép nhãn", "Label edge off"),
        ["up_count"] = ("Sai số con / hàng", "Wrong ups / rows"),
        ["up_pitch"] = ("Sai gap / bước (pitch)", "Wrong gap / pitch"),
        ["up_overlap"]= ("Chồng con", "Overlapping ups"),
        ["up_miss"]  = ("Thiếu con", "Missing ups"),
        ["rw_dir"]   = ("Sai hướng cuộn", "Wrong rewind direction"),
        ["rw_core"]  = ("Sai lõi", "Wrong core"),
        ["rw_tension"]= ("Tension lỏng / chặt", "Loose / tight tension"),
        ["rw_tele"]  = ("Telescoping / nhăn", "Telescoping / wrinkle"),
        ["mx_break"] = ("Đứt lưới thải", "Matrix break"),
        ["mx_stick"] = ("Dính con", "Sticking labels"),
        ["mx_left"]  = ("Sót thải", "Matrix residue left"),
        ["mx_tear"]  = ("Rách biên khi bóc", "Torn edge on stripping"),
        ["ct_wrong"] = ("Đếm sai", "Miscount"),
        ["ct_qty"]   = ("Thiếu / thừa con", "Under / over count"),
        ["ct_unit"]  = ("Sai đơn vị đóng gói", "Wrong pack unit"),
    };
}
