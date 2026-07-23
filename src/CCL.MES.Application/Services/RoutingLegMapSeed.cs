namespace CCL.MES.Application.Services;

/// <summary>
/// P11-1 — dữ liệu map mặc định "tín hiệu routing → leg" (mirror
/// <see cref="ProcessLineMapSeed"/>). DÙNG CHUNG bởi
/// <c>DbSeeder.SeedProcessLegMapAsync</c> (upsert idempotent NON-deleting)
/// và unit test resolver (cùng bộ luật → test khớp production).
///
/// Mỗi entry gắn THÊM (LegKind, Method) cạnh ProcessLine so với
/// ProcessLineMapSeed, để dựng leg DAG. WorkCenterPrefix (định danh máy)
/// là tín hiệu chính; OpKeyword là dự phòng.
///
/// <para><b>⚠ CẦN OPS XÁC NHẬN (STOP-gate breakdown §5.2)</b>: phân biệt
/// TAPE (cắt tape) vs CUT (cắt outline) khi CHẠY TRÊN CÙNG máy cắt
/// (RDC/PowerPunch/Magic) KHÔNG suy được từ WC prefix — vì WC prefix
/// (ưu tiên 2) khớp TRƯỚC OpKeyword (ưu tiên 3). Để TAPE/ASSEMBLY thắng,
/// routing thật cần mang <c>ProcessCode</c> (ưu tiên 1) HOẶC WorkCenter
/// riêng cho công đoạn tape. Các entry OpKeyword TAPE/ASSEMBLY dưới đây
/// chỉ thắng khi op KHÔNG khớp WC-prefix PRINT/CUT nào. Seed hiện đủ chạy
/// T1/T2/T3 với op có tín hiệu rõ; bộ keyword đầy đủ chờ Ops chốt.</para>
/// </summary>
public static class RoutingLegMapSeed
{
    // LegKind token (khớp Domain.Routing.LegKind.ToString()).
    public const string Print = "PRINT";
    public const string Cut = "CUT";
    public const string Tape = "TAPE";
    public const string Assembly = "ASSEMBLY";
    public const string PrintCut = "PRINT_CUT";

    // ProcessLine token — PHẢI khớp ProcessLineMapSeed / CheckItemLibrary.
    public const string Label = ProcessLineMapSeed.Label;
    public const string Digital = ProcessLineMapSeed.Digital;
    public const string Silk = ProcessLineMapSeed.Silk;
    public const string PressCnc = ProcessLineMapSeed.PressCnc;
    public const string Finishing = ProcessLineMapSeed.Finishing;

    public const string MatchProcessCode = "ProcessCode";
    public const string MatchWcPrefix = "WorkCenterPrefix";
    public const string MatchOpKeyword = "OpKeyword";

    public sealed record Entry(
        string MatchType, string MatchValue, string LegKind, string Method, string ProcessLine, int Sort, string? Note = null);

    public static IReadOnlyList<Entry> DefaultEntries() => _entries;

    private static readonly Entry[] _entries =
    {
        // ── PRINT (WorkCenterPrefix) ──────────────────────────────────
        // LABEL: Flexo Gallus/Brotech · Letterpress.
        new(MatchWcPrefix, "GFL", Print, "Flexo",       Label, 100),
        new(MatchWcPrefix, "BFL", Print, "Flexo",       Label, 101),
        new(MatchWcPrefix, "LP",  Print, "Letterpress", Label, 102),
        // DIGITAL: HP Indigo.
        new(MatchWcPrefix, "IDG", Print, "HP",          Digital, 110),
        // SILK: các máy in lụa (Sheet / Auto / R2R).
        new(MatchWcPrefix, "ASS",   Print, "Silkscreen", Silk, 120, "SS-Auto(Sheet)"),
        new(MatchWcPrefix, "MSS",   Print, "Silkscreen", Silk, 121, "SS(Sheet)"),
        new(MatchWcPrefix, "ARSS",  Print, "Silkscreen", Silk, 122),
        new(MatchWcPrefix, "MAGSS", Print, "Silkscreen", Silk, 123),
        new(MatchWcPrefix, "R2R",   Print, "Silkscreen", Silk, 124, "SS(R2R) roll silk"),

        // ── CUT (WorkCenterPrefix) — máy cắt/bế ───────────────────────
        new(MatchWcPrefix, "R2SC", Cut, "SheetCut",   PressCnc, 138, "SheetCut(SS) — cắt"),
        new(MatchWcPrefix, "FBL",  Cut, "Flatbed",    PressCnc, 130, "FB die-cut"),
        new(MatchWcPrefix, "PPSC", Cut, "PowerPunch", PressCnc, 131, "Power press"),
        new(MatchWcPrefix, "RDC",  Cut, "RDC",        PressCnc, 132),
        new(MatchWcPrefix, "ACNC", Cut, "CNC",        PressCnc, 133),
        new(MatchWcPrefix, "CNC",  Cut, "CNC",        PressCnc, 134),
        new(MatchWcPrefix, "LASE", Cut, "Laser",      PressCnc, 135),
        new(MatchWcPrefix, "PUNC", Cut, "Punching",   PressCnc, 136),
        new(MatchWcPrefix, "MDRH", Cut, "Drill",      PressCnc, 137),

        // ── PRINT fallback (OpKeyword) ────────────────────────────────
        new(MatchOpKeyword, "GALLUS",      Print, "Flexo",       Label, 200),
        new(MatchOpKeyword, "BROTECH",     Print, "Flexo",       Label, 201),
        new(MatchOpKeyword, "FLEXO",       Print, "Flexo",       Label, 202),
        new(MatchOpKeyword, "LETTERPRESS", Print, "Letterpress", Label, 203),
        new(MatchOpKeyword, "INDIGO",      Print, "HP",          Digital, 210),
        new(MatchOpKeyword, "SS(SHEET)",   Print, "Silkscreen",  Silk, 220),
        new(MatchOpKeyword, "SS-AUTO",     Print, "Silkscreen",  Silk, 221),
        new(MatchOpKeyword, "SS(R2R)",     Print, "Silkscreen",  Silk, 222),
        new(MatchOpKeyword, "SILKSCREEN",  Print, "Silkscreen",  Silk, 223),

        // ── CUT fallback (OpKeyword) ──────────────────────────────────
        new(MatchOpKeyword, "SHEETCUT",    Cut, "SheetCut",   PressCnc, 230),
        new(MatchOpKeyword, "POWER PRESS", Cut, "PowerPunch", PressCnc, 231),
        new(MatchOpKeyword, "MAGIC LINE",  Cut, "MagicLine",  PressCnc, 232),
        new(MatchOpKeyword, "CUT OUTLINE", Cut, "SheetCut",   PressCnc, 233),
        new(MatchOpKeyword, "CẮT OUTLINE", Cut, "SheetCut",   PressCnc, 234),

        // ── ASSEMBLY (OpKeyword) — ⚠ cần Ops xác nhận ─────────────────
        // Sort PHẢI nhỏ hơn keyword TAPE rộng (244) để "DÁN TAPE" thắng
        // "TAPE" khi cùng chuỗi op (specificity-wins, OrderBy Sort).
        new(MatchOpKeyword, "DÁN TAPE",   Assembly, "Assembly", Finishing, 236, "⚠ Ops confirm — phải < TAPE(244)"),
        new(MatchOpKeyword, "DÁN SEMI",   Assembly, "Assembly", Finishing, 237, "⚠ Ops confirm"),
        new(MatchOpKeyword, "ASSEMBLY",   Assembly, "Assembly", Finishing, 238, "⚠ Ops confirm"),
        new(MatchOpKeyword, "LẮP GHÉP",   Assembly, "Assembly", Finishing, 239, "⚠ Ops confirm"),

        // ── TAPE (OpKeyword) — ⚠ cần Ops xác nhận precedence vs máy cắt ─
        // Keyword cụ thể (CẮT TAPE…) trước; "TAPE" rộng để CUỐI (244).
        new(MatchOpKeyword, "CẮT TAPE",   Tape, "PowerPunch", Finishing, 240, "⚠ Ops confirm"),
        new(MatchOpKeyword, "CUT TAPE",   Tape, "PowerPunch", Finishing, 241, "⚠ Ops confirm"),
        new(MatchOpKeyword, "BĂNG KEO",   Tape, "PowerPunch", Finishing, 242, "⚠ Ops confirm"),
        new(MatchOpKeyword, "BĂNG DÍNH",  Tape, "PowerPunch", Finishing, 243, "⚠ Ops confirm"),
        new(MatchOpKeyword, "TAPE",       Tape, "PowerPunch", Finishing, 244, "⚠ Ops confirm — keyword rộng, để cuối"),

        // ── PRINT_CUT combined-inline (OpKeyword) ─────────────────────
        // "IN BẾ" phải thắng "FLEXO"(202) khi op vừa in vừa bế → Sort nhỏ hơn.
        new(MatchOpKeyword, "IN BẾ",       PrintCut, "FlexoInline", Label, 190, "in+bế 1 lượt (T1) — < FLEXO(202)"),
        new(MatchOpKeyword, "IN VÀ BẾ",    PrintCut, "FlexoInline", Label, 191),
        new(MatchOpKeyword, "PRINT & CUT", PrintCut, "FlexoInline", Label, 192),
        new(MatchOpKeyword, "PRINT AND CUT", PrintCut, "FlexoInline", Label, 193),
    };
}
