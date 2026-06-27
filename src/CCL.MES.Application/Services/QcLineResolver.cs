namespace CCL.MES.Application.Services;

/// <summary>
/// Phương án C — Bước 3. Resolver Routine → Process line.
///
/// Đọc tập <see cref="RoutingOp"/> của một mã hàng (RoutingOperations theo PartNo)
/// và suy ra tập QC line {LABEL · DIGITAL · SILK · PRESS_CNC} — khớp đúng
/// <see cref="CCL.MES.Domain.Entities.CheckItemLibrary.ProcessLine"/> để Bước 4
/// lấy subset thư viện materialize vào IPQC check.
///
/// <para><b>Vì sao KHÔNG dùng WorkCenter.Area:</b> field này auto-derive trong
/// <c>import_npi.py</c> bằng substring khá lỏng → sai trên dữ liệu thật (vd
/// Power press <c>PPSC1</c> bị gán "SILKSCREEN", <c>PUNC1/RDC12</c> = "OTHER").
/// Cũng KHÔNG dùng <c>RoutingType</c> (toàn "Manufacturing"). Nguồn tin cậy là
/// <c>Operation Description</c> (song ngữ "EN / VN") + tiền tố mã WorkCenter.</para>
///
/// <para>Thuần (pure) + tĩnh — không chạm DB; Bước 4 nạp routing rows rồi gọi
/// <see cref="Resolve"/>. Mã không phân loại được → vào <see cref="Resolution.Unmapped"/>
/// (caller log "unmapped process" + KHÔNG đoán, đúng ranh giới ở INDEX mục 3).</para>
/// </summary>
public static class QcLineResolver
{
    // QC line tokens — PHẢI khớp CheckItemLibrary.ProcessLine.
    public const string Label = "LABEL";
    public const string Digital = "DIGITAL";
    public const string Silk = "SILK";
    public const string PressCnc = "PRESS_CNC";

    // Kết quả phân loại 1 operation (nội bộ; "APPEARANCE" quy về LABEL line).
    public const string ClsLabel = "LABEL";
    public const string ClsDigital = "DIGITAL";
    public const string ClsSilk = "SILK";
    public const string ClsPressCnc = "PRESS_CNC";
    public const string ClsAppearance = "APPEARANCE"; // ép dán/cán màng → bộ appearance LABEL
    public const string ClsFqc = "FQC";
    public const string ClsOqc = "OQC";
    public const string ClsSkip = "SKIP";             // pre-press, sấy, tapping… không sinh QC line
    public const string ClsUnmapped = "UNMAPPED";

    /// <summary>1 operation trong routing (chỉ field cần cho phân loại).</summary>
    public sealed record RoutingOp(string? OpNo, string? Operation, string? WorkCenterNo, string? WorkCenterDesc);

    /// <summary>Kết quả resolve cho 1 mã hàng.</summary>
    public sealed record Resolution
    {
        /// <summary>Tập QC line in (LABEL/DIGITAL/SILK/PRESS_CNC) theo thứ tự ổn định.</summary>
        public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
        /// <summary>Có công đoạn FQC trong routing (mặc định mọi WO vẫn qua FQC — xem GHI CHÚ).</summary>
        public bool HasFqc { get; init; }
        /// <summary>Có công đoạn OQC trong routing.</summary>
        public bool HasOqc { get; init; }
        /// <summary>Operation không phân loại được — caller log + hỏi người duyệt, không đoán.</summary>
        public IReadOnlyList<string> Unmapped { get; init; } = Array.Empty<string>();
    }

    // Thứ tự hiển thị/ổn định của QC line.
    private static readonly string[] LineOrder = { Label, Digital, Silk, PressCnc };

    /// <summary>Suy tập QC line + cờ FQC/OQC từ toàn bộ routing của 1 mã hàng.</summary>
    public static Resolution Resolve(IEnumerable<RoutingOp> ops)
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);
        var unmapped = new List<string>();
        var hasFqc = false;
        var hasOqc = false;

        foreach (var op in ops ?? Array.Empty<RoutingOp>())
        {
            switch (Classify(op))
            {
                case ClsLabel: lines.Add(Label); break;
                case ClsDigital: lines.Add(Digital); break;
                case ClsSilk: lines.Add(Silk); break;
                case ClsPressCnc: lines.Add(PressCnc); break;
                case ClsAppearance: lines.Add(Label); break; // appearance dùng bộ LABEL
                case ClsFqc: hasFqc = true; break;
                case ClsOqc: hasOqc = true; break;
                case ClsSkip: break;
                default:
                    unmapped.Add(Signature(op));
                    break;
            }
        }

        var ordered = LineOrder.Where(lines.Contains).ToList();
        return new Resolution
        {
            Lines = ordered,
            HasFqc = hasFqc,
            HasOqc = hasOqc,
            Unmapped = unmapped,
        };
    }

    /// <summary>Phân loại 1 operation. Ưu tiên Operation Description; lùi về tiền tố
    /// mã WorkCenter khi mô tả không quyết định được. Thứ tự kiểm CÓ Ý NGHĨA
    /// (vd "LAM.&amp;CUT" phải ra PRESS_CNC, không phải APPEARANCE).</summary>
    public static string Classify(RoutingOp op)
    {
        var desc = Up(op?.Operation);
        var wc = Up(op?.WorkCenterNo);
        var wcDesc = Up(op?.WorkCenterDesc);

        // 1) QC stage cuối luồng — nhận diện trước (MAN1=FQC, MAN2=OQC).
        if (ContainsAny(desc, "OQC") || wc.StartsWith("MAN2")) return ClsOqc;
        if (ContainsAny(desc, "FQC") || wc.StartsWith("MAN1")) return ClsFqc;

        // 2) Công đoạn KHÔNG sinh QC line: chuẩn bị, sấy, dán băng, manual phụ.
        if (ContainsAny(desc, "PRE-", "PRE PREPARE", "PREPARE", "CHUẨN BỊ",
                              "BAKING", "OVEN", "SẤY", "UV TUNNEL", "SURFACE DRY",
                              "TAPPING", "DÁN BĂNG")) return ClsSkip;
        if (wc.StartsWith("FXPP") || wc.StartsWith("OVS") || wc.StartsWith("UVS")) return ClsSkip;

        // 3) Cắt / dập / khoan / press — PRESS_CNC. KIỂM TRƯỚC laminate vì "LAM.&CUT".
        if (ContainsAny(desc, "CUT", "CẮT", "PUNCH", "ĐỤC", "DRILL", "KHOAN",
                              "POWER PRESS", "(PRESS) CUT", "RDC", "DIE CUT", "DIECUT",
                              "LASER", "CNC", "ROUTING", "SLIT", "XẺ"))
            return ClsPressCnc;
        if (wc.StartsWith("FBL") || wc.StartsWith("RDC") || wc.StartsWith("PPSC") ||
            wc.StartsWith("PUNC") || wc.StartsWith("MDRH") || wc.StartsWith("CNC") ||
            wc.StartsWith("ACNC") || wc.StartsWith("LASE") || wc.StartsWith("KISS"))
            return ClsPressCnc;

        // 4) In số (digital) — Indigo / HP / KTS / Zebra.
        if (ContainsAny(desc, "INDIGO", "HP INDIGO", "DIGITAL", "KTS", "MÁY KTS", "ZEBRA", "THERMAL"))
            return ClsDigital;
        if (wc.StartsWith("IDG") || wc.StartsWith("HP")) return ClsDigital;

        // 5) In lụa (silk/screen).
        if (ContainsAny(desc, "SILK", "(SILK)", "SCREEN", "LỤA"))
            return ClsSilk;
        if (wc.StartsWith("ASS") || wc.StartsWith("MSS") || wc.StartsWith("ARSS") ||
            wc.StartsWith("MAGSS") || (wc.StartsWith("SS") && !wc.StartsWith("SSC")))
            return ClsSilk;

        // 6) In nhãn flexo / letterpress.
        if (ContainsAny(desc, "GALLUS", "BROTECH", "FLEXO", "LETTERPRESS"))
            return ClsLabel;
        if (wc.StartsWith("GFL") || wc.StartsWith("BFL") || wc.StartsWith("LP"))
            return ClsLabel;

        // 7) Cán màng / ép dán / magic → appearance (bộ LABEL). SAU cắt.
        if (ContainsAny(desc, "LAM", "ÉP DÁN", "LAMINAT", "MAGIC") ||
            wc.StartsWith("LAM") || wc.StartsWith("MAG"))
            return ClsAppearance;

        // 8) "PRINT / In nhãn" chung chung còn lại (không rõ công nghệ) → LABEL.
        if (ContainsAny(desc, "PRINT", "IN NHÃN")) return ClsLabel;

        return ClsUnmapped;
    }

    private static string Signature(RoutingOp? op) =>
        $"op={op?.OpNo} wc={op?.WorkCenterNo} desc={op?.Operation}";

    private static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
            if (haystack.Contains(n, StringComparison.Ordinal)) return true;
        return false;
    }
}
