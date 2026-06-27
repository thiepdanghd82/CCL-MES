namespace CCL.MES.Application.Services;

/// <summary>
/// Phương án C — Bước 6. Dữ liệu map mặc định process→line (quyết định #5 + INDEX §3
/// + ProcessCatalog). DÙNG CHUNG bởi <c>DbSeeder.SeedProcessLineMapAsync</c> (upsert
/// idempotent) và unit test resolver (cùng một bộ luật → test khớp production).
///
/// Map CHỦ YẾU theo <b>WorkCenterPrefix</b> (định danh máy) để tránh nhập nhằng
/// động từ "CUT" (vd op "(PRESS) LAM.&amp;Cut" trên máy SheetCut(SS) là SILK theo
/// quyết định #5, không phải CUT). OpKeyword là dự phòng khi WC prefix lạ.
/// </summary>
public static class ProcessLineMapSeed
{
    public const string Label = "LABEL";
    public const string Digital = "DIGITAL";
    public const string Silk = "SILK";
    public const string PressCnc = "PRESS_CNC";
    public const string Finishing = "FINISHING"; // Q2 — cán/ép dán/xẻ (line thứ 5)
    public const string None = "NONE";

    public const string MatchProcessCode = "ProcessCode";
    public const string MatchWcPrefix = "WorkCenterPrefix";
    public const string MatchOpKeyword = "OpKeyword";

    public sealed record Entry(string MatchType, string MatchValue, string QcLine, int Sort, string? Note = null);

    /// <summary>Bộ luật mặc định (natural key = (MatchType, MatchValue)).</summary>
    public static IReadOnlyList<Entry> DefaultEntries() => _entries;

    private static readonly Entry[] _entries =
    {
        // ── WorkCenterPrefix (định danh máy) — Sort 100+ ──────────────
        // LABEL: Flexo Gallus/Brotech · Letterpress.
        new(MatchWcPrefix, "GFL", Label, 100),
        new(MatchWcPrefix, "BFL", Label, 101),
        new(MatchWcPrefix, "LP",  Label, 102, "Letterpress"),
        // DIGITAL: HP Indigo · Zebra.
        new(MatchWcPrefix, "IDG", Digital, 110),
        // SILK: SS(Sheet) · SS-Auto · SS(R2R). (Q1: SheetCut(SS) KHÔNG phải in lụa → PRESS_CNC.)
        new(MatchWcPrefix, "ASS",   Silk, 120, "SS-Auto(Sheet)"),
        new(MatchWcPrefix, "MSS",   Silk, 121, "SS(Sheet)"),
        new(MatchWcPrefix, "ARSS",  Silk, 122),
        new(MatchWcPrefix, "MAGSS", Silk, 123),
        new(MatchWcPrefix, "R2R",   Silk, 124, "SS(R2R) roll silk"),
        // Q1: máy SheetCut (R2SC*) là công đoạn CẮT → PRESS_CNC (longest-match thắng R2R nếu có).
        new(MatchWcPrefix, "R2SC",  PressCnc, 138, "SheetCut(SS) — cắt, không in lụa"),
        // PRESS_CNC: FB · Power press · RDC · CNC · Laser · Punching · Drill.
        new(MatchWcPrefix, "FBL",  PressCnc, 130, "FB die-cut"),
        new(MatchWcPrefix, "PPSC", PressCnc, 131, "Power press"),
        new(MatchWcPrefix, "RDC",  PressCnc, 132),
        new(MatchWcPrefix, "ACNC", PressCnc, 133),
        new(MatchWcPrefix, "CNC",  PressCnc, 134),
        new(MatchWcPrefix, "LASE", PressCnc, 135, "Laser"),
        new(MatchWcPrefix, "PUNC", PressCnc, 136, "Punching"),
        new(MatchWcPrefix, "MDRH", PressCnc, 137, "Drill (CNC)"),
        // appearance LABEL: Laminate · Magic (longest-match cho MAGSS→SILK ở trên).
        // Q2: cán/ép dán/magic → line FINISHING riêng (KHÔNG dội bộ print LABEL/SILK).
        new(MatchWcPrefix, "LAM", Finishing, 140, "Laminate (Roll/Label) → FINISHING"),
        new(MatchWcPrefix, "MAG", Finishing, 141, "Magic (LAM.&Slit) → FINISHING"),
        // NONE: pre-press · sấy · manual · FQC · OQC (không sinh item IPQC).
        new(MatchWcPrefix, "FXPP", None, 150, "Pre-press"),
        new(MatchWcPrefix, "OVS",  None, 151, "Oven drying"),
        new(MatchWcPrefix, "UVS",  None, 152, "UV drying"),
        new(MatchWcPrefix, "MAN1", None, 153, "FQC & Packaging"),
        new(MatchWcPrefix, "MAN2", None, 154, "OQC"),
        new(MatchWcPrefix, "MAN3", None, 155, "Manual / Tapping"),

        // ── OpKeyword (dự phòng; khớp Operation HOẶC WorkCenterDescription) — Sort 200+ ──
        new(MatchOpKeyword, "GALLUS",      Label, 200),
        new(MatchOpKeyword, "BROTECH",     Label, 201),
        new(MatchOpKeyword, "FLEXO",       Label, 202),
        new(MatchOpKeyword, "LETTERPRESS", Label, 203),
        new(MatchOpKeyword, "INDIGO",      Digital, 210),
        new(MatchOpKeyword, "ZEBRA",       Digital, 211, "thermal/variable — xác nhận"),
        new(MatchOpKeyword, "SS(SHEET)",   Silk, 220),
        new(MatchOpKeyword, "SS-AUTO",     Silk, 221),
        new(MatchOpKeyword, "SS(R2R)",     Silk, 222),
        new(MatchOpKeyword, "SHEETCUT",    PressCnc, 223), // Q1: SheetCut = cắt, không SILK
        new(MatchOpKeyword, "POWER PRESS", PressCnc, 230),
        new(MatchOpKeyword, "RDC",         PressCnc, 231),
        new(MatchOpKeyword, "LASER",       PressCnc, 232),
        new(MatchOpKeyword, "PUNCH",       PressCnc, 233),
        new(MatchOpKeyword, "DRILL",       PressCnc, 234),
        // Q2: cán/ép dán/xẻ → FINISHING (gồm "SILK LAMINATION" — máy cán, không phải in lụa).
        new(MatchOpKeyword, "LAMINAT",     Finishing, 240),
        new(MatchOpKeyword, "LAMINATION",  Finishing, 241),
        new(MatchOpKeyword, "ÉP DÁN",      Finishing, 242),
        new(MatchOpKeyword, "MAGIC",       Finishing, 243),
        new(MatchOpKeyword, "SLIT",        Finishing, 244),
        new(MatchOpKeyword, "XẺ",          Finishing, 245),
        new(MatchOpKeyword, "PRE- PREPARE",None, 250),
        new(MatchOpKeyword, "PRE-PRESS",   None, 251),
        new(MatchOpKeyword, "BAKING",      None, 252),
        new(MatchOpKeyword, "OVEN",        None, 253),
        new(MatchOpKeyword, "UV TUNNEL",   None, 254),
        new(MatchOpKeyword, "TAPPING",     None, 255),
        new(MatchOpKeyword, "INK MIX",     None, 256),
        new(MatchOpKeyword, "AOI",         None, 257),
        new(MatchOpKeyword, "FQC",         None, 258),
        new(MatchOpKeyword, "OQC",         None, 259),
    };
}
