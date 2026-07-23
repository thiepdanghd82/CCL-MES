using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;

namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11-2 — luật tiến phase của MỘT leg + gate phụ thuộc DAG (pure).
///
/// LegFlow (subset production tuyến tính; QA_PENDING/PAUSED để P11-3+):
///   PREPRESS → SETTING → IPQC_WAIT → IPQC_APPROVED → RUNNING → LEG_DONE
///
/// Gate phụ thuộc (Q4):
///   • SOFT (PRINT∥TAPE, CUT-after-PRINT) — advisory, KHÔNG chặn; trả cờ
///     <see cref="AdvanceCheck.SoftWaiting"/> để UI cảnh báo.
///   • HARD (ASSEMBLY cần semi) — CHẶN khi leg muốn vào phase "sản xuất"
///     (<see cref="RUNNING"/>) mà predecessor HARD chưa <c>LEG_DONE</c>
///     (+ đủ qty theo <c>WoLegDependency.RequiredQty</c>).
/// </summary>
public static class RoutingLegGate
{
    public static readonly LegPhase[] LegFlow =
    {
        LegPhase.PREPRESS, LegPhase.SETTING, LegPhase.IPQC_WAIT,
        LegPhase.IPQC_APPROVED, LegPhase.RUNNING, LegPhase.LEG_DONE,
    };

    public sealed record AdvanceCheck(bool Allowed, WoErrorCode? Error, bool SoftWaiting);

    /// <summary>Phase kế tiếp trong LegFlow, null nếu đã cuối.</summary>
    public static LegPhase? Next(LegPhase current)
    {
        var i = Array.IndexOf(LegFlow, current);
        return (i < 0 || i >= LegFlow.Length - 1) ? null : LegFlow[i + 1];
    }

    public static bool TryParse(string? s, out LegPhase phase)
        => Enum.TryParse(s, ignoreCase: true, out phase);

    /// <summary>
    /// Kiểm tra leg <paramref name="leg"/> có được phép chuyển sang
    /// <paramref name="toPhase"/> không. Chỉ cho tiến 1 bước theo LegFlow
    /// (hoặc giữ nguyên bước cuối). Gate phụ thuộc áp khi bước vào RUNNING.
    /// </summary>
    public static AdvanceCheck CanEnter(WorkOrder wo, WoLeg leg, LegPhase toPhase)
    {
        if (!TryParse(leg.LegPhase, out var from))
            return new AdvanceCheck(false, WoErrorCode.InvalidStepTransition, false);

        // Chỉ cho tiến đúng 1 bước theo LegFlow.
        if (Next(from) != toPhase)
            return new AdvanceCheck(false, WoErrorCode.InvalidStepTransition, false);

        // Gate phụ thuộc — kiểm khi leg bắt đầu "chạy" (vào RUNNING).
        if (toPhase == LegPhase.RUNNING)
        {
            var preds = wo.LegEdges.Where(e => e.LegId == leg.Id).ToList();
            bool softWaiting = false;
            foreach (var edge in preds)
            {
                var pred = wo.Legs.FirstOrDefault(l => l.Id == edge.DependsOnLegId);
                if (pred is null) continue;
                var predDone = string.Equals(pred.LegPhase, nameof(LegPhase.LEG_DONE), StringComparison.Ordinal);
                var qtyOk = edge.RequiredQty <= 0 || pred.QtyDoneCached >= edge.RequiredQty;

                if (string.Equals(edge.DependencyGate, nameof(DependencyGate.HARD), StringComparison.OrdinalIgnoreCase))
                {
                    if (!predDone || !qtyOk)
                        return new AdvanceCheck(false, WoErrorCode.AssemblyInputsMissing, false);
                }
                else // SOFT — advisory
                {
                    if (!predDone) softWaiting = true;
                }
            }
            return new AdvanceCheck(true, null, softWaiting);
        }

        return new AdvanceCheck(true, null, false);
    }
}
