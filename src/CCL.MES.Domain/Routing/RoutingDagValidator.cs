using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;

namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11-1 — validator thuần (không I/O) cho routing DAG của một
/// <see cref="WorkOrder"/> nhiều leg. Gọi bởi
/// <c>WorkOrderStateMachine.CheckCondition</c> tại cell
/// <c>PREPRESS → SPLIT</c> và bởi P11-2 lúc dựng WO. Fail-closed: bất kỳ
/// vi phạm nào → (false, mã lỗi cụ thể).
///
/// Ba luật (breakdown §6):
/// <list type="number">
///   <item><b>No cycle</b> — topological sort (Kahn) phải rút hết leg.</item>
///   <item><b>Assembly inputs</b> — mọi leg ASSEMBLY <c>InputSource=IN_LINE</c>
///     phải có ≥1 dep PRINT và ≥1 dep TAPE.</item>
///   <item><b>Terminal reaches FQC</b> — phải có ≥1 leg terminal (không
///     successor); cạnh không trỏ leg lạ.</item>
/// </list>
/// Leg song song (2 print∥cut cùng đổ về FQC, không cạnh) là HỢP LỆ —
/// chúng là terminal, join tại FQC.
/// </summary>
public static class RoutingDagValidator
{
    public static (bool ok, WoErrorCode? error) IsValid(WorkOrder wo)
    {
        var legs = wo.Legs;
        var edges = wo.LegEdges;
        if (legs.Count == 0) return (false, WoErrorCode.InvalidRoutingDag);

        var legIds = new HashSet<long>(legs.Select(l => l.Id));

        // Cạnh phải trỏ leg tồn tại + không tự vòng.
        foreach (var e in edges)
        {
            if (!legIds.Contains(e.LegId) || !legIds.Contains(e.DependsOnLegId))
                return (false, WoErrorCode.InvalidRoutingDag);
            if (e.LegId == e.DependsOnLegId)
                return (false, WoErrorCode.InvalidRoutingDag);
        }

        // (1) No cycle — Kahn topo sort trên cạnh DependsOn → Leg.
        if (HasCycle(legIds, edges)) return (false, WoErrorCode.InvalidRoutingDag);

        // (2) Assembly inputs — ≥1 PRINT + ≥1 TAPE predecessor (IN_LINE).
        foreach (var leg in legs)
        {
            if (!IsKind(leg, LegKind.ASSEMBLY)) continue;
            if (!IsSource(leg, InputSource.IN_LINE)) continue; // FROM_STOCK ⇒ P11.5 kiểm sau

            var preds = edges.Where(e => e.LegId == leg.Id)
                             .Select(e => LegById(legs, e.DependsOnLegId))
                             .Where(l => l is not null)
                             .Select(l => l!)
                             .ToList();
            bool hasPrint = preds.Any(l => IsKind(l, LegKind.PRINT));
            bool hasTape  = preds.Any(l => IsKind(l, LegKind.TAPE));
            if (!hasPrint || !hasTape)
                return (false, WoErrorCode.AssemblyInputsMissing);
        }

        // (3) ≥1 terminal leg (không là predecessor của cạnh nào).
        bool anyTerminal = legs.Any(l => edges.All(e => e.DependsOnLegId != l.Id));
        if (!anyTerminal) return (false, WoErrorCode.InvalidRoutingDag);

        return (true, null);
    }

    private static bool HasCycle(HashSet<long> legIds, List<WoLegDependency> edges)
    {
        // indegree = số predecessor của mỗi leg.
        var indeg = legIds.ToDictionary(id => id, _ => 0);
        foreach (var e in edges) indeg[e.LegId]++;

        var queue = new Queue<long>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        int visited = 0;
        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            visited++;
            foreach (var e in edges.Where(e => e.DependsOnLegId == n))
            {
                if (--indeg[e.LegId] == 0) queue.Enqueue(e.LegId);
            }
        }
        return visited != legIds.Count; // còn leg chưa rút = có chu trình
    }

    private static WoLeg? LegById(List<WoLeg> legs, long id) => legs.FirstOrDefault(l => l.Id == id);

    private static bool IsKind(WoLeg leg, LegKind kind) =>
        string.Equals(leg.LegKind, kind.ToString(), StringComparison.OrdinalIgnoreCase);

    private static bool IsSource(WoLeg leg, InputSource src) =>
        string.Equals(leg.InputSource, src.ToString(), StringComparison.OrdinalIgnoreCase);
}
