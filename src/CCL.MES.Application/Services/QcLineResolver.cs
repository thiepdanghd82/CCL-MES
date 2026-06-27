namespace CCL.MES.Application.Services;

/// <summary>
/// Phương án C — Bước 3 (data-driven sau finding #3/#7). Resolver Routine → Process line.
///
/// Đọc tập <see cref="RoutingOp"/> của một mã hàng + bảng map
/// (<see cref="MapEntry"/> nạp từ <c>ProcessLineMap</c> qua <see cref="ProcessLineMapSeed"/>)
/// → suy tập QC line {LABEL · DIGITAL · SILK · PRESS_CNC}. Map là DỮ LIỆU (sửa qua seed,
/// quyết định #5) — KHÔNG còn keyword/StartsWith hardcode trong code.
///
/// <para>Ưu tiên khớp 1 op: ProcessCode (chính xác) → WorkCenterPrefix (DÀI nhất) →
/// OpKeyword (chứa, Sort nhỏ thắng) → Unmapped. QcLine=NONE = hợp lệ (pre-press/sấy/
/// FQC/OQC…), KHÔNG sinh item + KHÔNG vào Unmapped. Op không dòng nào khớp → Unmapped
/// (caller log loud + hỏi người duyệt, KHÔNG đoán).</para>
/// </summary>
public static class QcLineResolver
{
    // QC line tokens — PHẢI khớp CheckItemLibrary.ProcessLine + ProcessLineMapSeed.
    public const string Label = ProcessLineMapSeed.Label;
    public const string Digital = ProcessLineMapSeed.Digital;
    public const string Silk = ProcessLineMapSeed.Silk;
    public const string PressCnc = ProcessLineMapSeed.PressCnc;
    public const string None = ProcessLineMapSeed.None;
    public const string Unmapped = "UNMAPPED";

    /// <summary>1 operation trong routing (chỉ field cần cho phân loại).
    /// <paramref name="ProcessCode"/> tùy chọn (routing IFS hiện chưa có cột này).</summary>
    public sealed record RoutingOp(
        string? OpNo, string? Operation, string? WorkCenterNo, string? WorkCenterDesc, string? ProcessCode = null);

    /// <summary>1 luật map (chiếu từ ProcessLineMap entity / ProcessLineMapSeed.Entry).</summary>
    public sealed record MapEntry(string MatchType, string MatchValue, string QcLine, int Sort);

    /// <summary>Kết quả resolve cho 1 mã hàng.</summary>
    public sealed record Resolution
    {
        /// <summary>Tập QC line in (LABEL/DIGITAL/SILK/PRESS_CNC) theo thứ tự ổn định.</summary>
        public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();
        /// <summary>Operation không phân loại được — caller log + hỏi người duyệt, không đoán.</summary>
        public IReadOnlyList<string> Unmapped { get; init; } = Array.Empty<string>();
    }

    private static readonly string[] LineOrder = { Label, Digital, Silk, PressCnc };

    /// <summary>Chiếu từ seed sang MapEntry (cho unit test dùng đúng bộ luật production).</summary>
    public static IReadOnlyList<MapEntry> MapFromSeed() =>
        ProcessLineMapSeed.DefaultEntries()
            .Select(e => new MapEntry(e.MatchType, e.MatchValue, e.QcLine, e.Sort))
            .ToList();

    /// <summary>Suy tập QC line từ toàn bộ routing của 1 mã hàng + bảng map.</summary>
    public static Resolution Resolve(IEnumerable<RoutingOp> ops, IReadOnlyList<MapEntry> map)
    {
        var lines = new HashSet<string>(StringComparer.Ordinal);
        var unmapped = new List<string>();

        foreach (var op in ops ?? Array.Empty<RoutingOp>())
        {
            var line = Classify(op, map);
            if (line == Unmapped) { unmapped.Add(Signature(op)); continue; }
            if (line == None) continue;            // hợp lệ — không sinh item
            lines.Add(line);
        }

        return new Resolution
        {
            Lines = LineOrder.Where(lines.Contains).ToList(),
            Unmapped = unmapped,
        };
    }

    /// <summary>Phân loại 1 operation theo bảng map. Trả QcLine (LABEL/…/NONE) hoặc
    /// <see cref="Unmapped"/>. Ưu tiên: ProcessCode → WorkCenterPrefix(dài nhất) → OpKeyword(Sort nhỏ).</summary>
    public static string Classify(RoutingOp op, IReadOnlyList<MapEntry> map)
    {
        if (map is null || map.Count == 0) return Unmapped;

        var proc = Up(op?.ProcessCode);
        var wc = Up(op?.WorkCenterNo);
        var hay = Up(op?.Operation) + "  " + Up(op?.WorkCenterDesc); // gộp để Contains

        // 1) ProcessCode khớp chính xác.
        if (proc.Length > 0)
        {
            var hit = map.Where(m => Eq(m.MatchType, ProcessLineMapSeed.MatchProcessCode)
                                  && string.Equals(Up(m.MatchValue), proc, StringComparison.Ordinal))
                         .OrderBy(m => m.Sort).FirstOrDefault();
            if (hit is not null) return hit.QcLine;
        }

        // 2) WorkCenterPrefix — khớp DÀI nhất (tie → Sort nhỏ).
        if (wc.Length > 0)
        {
            var hit = map.Where(m => Eq(m.MatchType, ProcessLineMapSeed.MatchWcPrefix)
                                  && m.MatchValue.Length > 0
                                  && wc.StartsWith(Up(m.MatchValue), StringComparison.Ordinal))
                         .OrderByDescending(m => m.MatchValue.Length).ThenBy(m => m.Sort)
                         .FirstOrDefault();
            if (hit is not null) return hit.QcLine;
        }

        // 3) OpKeyword — chứa (trong Operation hoặc WorkCenterDescription), Sort nhỏ thắng.
        var kw = map.Where(m => Eq(m.MatchType, ProcessLineMapSeed.MatchOpKeyword)
                             && m.MatchValue.Length > 0
                             && hay.Contains(Up(m.MatchValue), StringComparison.Ordinal))
                    .OrderBy(m => m.Sort).FirstOrDefault();
        if (kw is not null) return kw.QcLine;

        return Unmapped;
    }

    private static string Signature(RoutingOp? op) =>
        $"op={op?.OpNo} wc={op?.WorkCenterNo} desc={op?.Operation}";

    private static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();
    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
