namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11-1 — resolver thuần: tập routing op của một mã hàng + bảng
/// <c>ProcessLegMap</c> → kế hoạch leg DAG (nodes + edges). Tái dùng
/// đúng thuật toán ưu tiên của <c>QcLineResolver.Classify</c>
/// (ProcessCode chính xác → WorkCenterPrefix DÀI nhất → OpKeyword chứa,
/// Sort nhỏ thắng). Op không map → <see cref="LegPlan.Unmapped"/>
/// (caller log loud + hỏi người duyệt, KHÔNG đoán).
///
/// Topology (breakdown §1.2) tự phát sinh:
/// <list type="bullet">
///   <item>T1: 1 op PRINT_CUT → 1 leg, 0 cạnh.</item>
///   <item>T2: PRINT → CUT → 1 cạnh SOFT.</item>
///   <item>T3: PRINT ∥ TAPE → ASSEMBLY (2 cạnh HARD) → CUT (1 cạnh SOFT).</item>
/// </list>
/// KHÔNG chạm DbContext — P11-2 lấy <see cref="LegPlan"/> để tạo
/// <c>WoLeg</c> + <c>WoLegDependency</c>.
/// </summary>
public static class RoutingLegResolver
{
    /// <summary>1 operation routing (chỉ field cần cho phân loại) — cùng
    /// hình dạng với <c>QcLineResolver.RoutingOp</c>.</summary>
    public sealed record RoutingOp(
        string? OpNo, string? Operation, string? WorkCenterNo, string? WorkCenterDesc, string? ProcessCode = null);

    /// <summary>1 luật map (chiếu từ ProcessLegMap / seed).</summary>
    public sealed record MapEntry(string MatchType, string MatchValue, string LegKind, string Method, string ProcessLine, int Sort);

    public sealed record LegNode(int Sequence, string LegKind, string Method, string ProcessLine);

    public sealed record LegEdge(int FromSeq, int ToSeq, DependencyGate Gate);

    public sealed record LegPlan(
        IReadOnlyList<LegNode> Legs,
        IReadOnlyList<LegEdge> Edges,
        IReadOnlyList<string> Unmapped);

    public const string MatchProcessCode = "ProcessCode";
    public const string MatchWcPrefix = "WorkCenterPrefix";
    public const string MatchOpKeyword = "OpKeyword";

    /// <summary>Suy kế hoạch leg DAG từ toàn bộ routing của 1 mã hàng.</summary>
    public static LegPlan Resolve(IEnumerable<RoutingOp> ops, IReadOnlyList<MapEntry> map)
    {
        var unmapped = new List<string>();
        var classified = new List<(LegKind kind, string method, string line)>();

        foreach (var op in ops ?? Array.Empty<RoutingOp>())
        {
            var hit = Classify(op, map);
            if (hit is null) { unmapped.Add(Signature(op)); continue; }
            classified.Add(hit.Value);
        }

        // Gộp op liền kề cùng (LegKind, ProcessLine, Method) → 1 leg.
        var legs = new List<LegNode>();
        foreach (var c in classified)
        {
            var last = legs.Count > 0 ? legs[^1] : null;
            if (last is not null
                && last.LegKind == c.kind.ToString()
                && last.ProcessLine == c.line
                && last.Method == c.method)
            {
                continue; // gộp vào leg trước
            }
            legs.Add(new LegNode(legs.Count, c.kind.ToString(), c.method, c.line));
        }

        var edges = BuildEdges(legs);
        return new LegPlan(legs, edges, unmapped);
    }

    // Dựng cạnh DAG theo semantics fork-join: PRINT/TAPE là semi song
    // song; ASSEMBLY tiêu thụ HARD mọi semi đứng trước; CUT phụ thuộc
    // SOFT vào semi (T2) hoặc vào leg chính gần nhất (assembly — T3).
    private static List<LegEdge> BuildEdges(List<LegNode> legs)
    {
        var edges = new List<LegEdge>();
        var pendingSemi = new List<LegNode>();  // PRINT/TAPE chưa bị assembly/cut tiêu thụ
        LegNode? lastMain = null;               // ASSEMBLY/CUT/PRINT_CUT gần nhất

        foreach (var leg in legs)
        {
            switch (leg.LegKind)
            {
                case nameof(LegKind.PRINT):
                case nameof(LegKind.TAPE):
                    pendingSemi.Add(leg);
                    break;

                case nameof(LegKind.ASSEMBLY):
                    foreach (var s in pendingSemi)
                        edges.Add(new LegEdge(s.Sequence, leg.Sequence, DependencyGate.HARD));
                    if (pendingSemi.Count == 0 && lastMain is not null)
                        edges.Add(new LegEdge(lastMain.Sequence, leg.Sequence, DependencyGate.SOFT));
                    pendingSemi.Clear();
                    lastMain = leg;
                    break;

                case nameof(LegKind.CUT):
                    if (pendingSemi.Count > 0)
                    {
                        foreach (var s in pendingSemi)  // T2: cắt tiêu thụ trực tiếp print
                            edges.Add(new LegEdge(s.Sequence, leg.Sequence, DependencyGate.SOFT));
                        pendingSemi.Clear();
                    }
                    else if (lastMain is not null)
                    {
                        edges.Add(new LegEdge(lastMain.Sequence, leg.Sequence, DependencyGate.SOFT));
                    }
                    lastMain = leg;
                    break;

                case nameof(LegKind.PRINT_CUT):
                    lastMain = leg;  // T1 combined-inline — thường là leg duy nhất
                    break;
            }
        }
        return edges;
    }

    /// <summary>Phân loại 1 op → (LegKind, Method, ProcessLine) hoặc null
    /// (unmapped). Ưu tiên giống <c>QcLineResolver.Classify</c>.</summary>
    public static (LegKind kind, string method, string line)? Classify(RoutingOp op, IReadOnlyList<MapEntry> map)
    {
        if (map is null || map.Count == 0) return null;

        var proc = Up(op?.ProcessCode);
        var wc = Up(op?.WorkCenterNo);
        var hay = Up(op?.Operation) + "  " + Up(op?.WorkCenterDesc);

        // 1) ProcessCode khớp chính xác.
        if (proc.Length > 0)
        {
            var hit = map.Where(m => Eq(m.MatchType, MatchProcessCode)
                                  && string.Equals(Up(m.MatchValue), proc, StringComparison.Ordinal))
                         .OrderBy(m => m.Sort).FirstOrDefault();
            if (hit is not null) return Project(hit);
        }

        // 2) WorkCenterPrefix — khớp DÀI nhất (tie → Sort nhỏ).
        if (wc.Length > 0)
        {
            var hit = map.Where(m => Eq(m.MatchType, MatchWcPrefix)
                                  && m.MatchValue.Length > 0
                                  && wc.StartsWith(Up(m.MatchValue), StringComparison.Ordinal))
                         .OrderByDescending(m => m.MatchValue.Length).ThenBy(m => m.Sort)
                         .FirstOrDefault();
            if (hit is not null) return Project(hit);
        }

        // 3) OpKeyword — chứa (Operation hoặc WorkCenterDesc), Sort nhỏ thắng.
        var kw = map.Where(m => Eq(m.MatchType, MatchOpKeyword)
                             && m.MatchValue.Length > 0
                             && hay.Contains(Up(m.MatchValue), StringComparison.Ordinal))
                    .OrderBy(m => m.Sort).FirstOrDefault();
        if (kw is not null) return Project(kw);

        return null;
    }

    private static (LegKind, string, string)? Project(MapEntry m) =>
        Enum.TryParse<LegKind>(m.LegKind, ignoreCase: true, out var k)
            ? (k, m.Method, m.ProcessLine)
            : null;

    private static string Signature(RoutingOp? op) =>
        $"op={op?.OpNo} wc={op?.WorkCenterNo} desc={op?.Operation}";

    private static string Up(string? s) => (s ?? "").Trim().ToUpperInvariant();
    private static bool Eq(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
