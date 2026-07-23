using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.Routing;

/// <summary>
/// P11.5 — planner thuần cho decoupling point: chọn lô semi từ kho theo
/// FEFO (First-Expired-First-Out) đủ số lượng assembly cần, + gate
/// "assembly đã đủ input từ kho chưa". Không I/O — controller (P11.5-2)
/// nạp lô + ghi <see cref="SemiAllocation"/> trong 1 SaveChanges.
/// </summary>
public static class SemiStockAllocator
{
    /// <summary>1 dòng kế hoạch xuất: lấy <paramref name="Qty"/> từ lô
    /// <paramref name="SemiLotId"/>.</summary>
    public sealed record Pick(long SemiLotId, string LotNo, int Qty);

    public sealed record Plan(IReadOnlyList<Pick> Picks, int Allocated, int Shortfall);

    /// <summary>
    /// Lập kế hoạch xuất <paramref name="requiredQty"/> đơn vị semi
    /// <paramref name="semiKind"/> (khớp <paramref name="specRevisionId"/>)
    /// từ <paramref name="lots"/> theo FEFO. Bỏ lô EXPIRED / hết
    /// QtyAvailable. Nếu tổng khả dụng &lt; yêu cầu → <c>Shortfall &gt; 0</c>
    /// (caller từ chối reserve, không partial-commit).
    /// </summary>
    public static Plan Fefo(IEnumerable<SemiLot> lots, string semiKind, long? specRevisionId, int requiredQty)
    {
        var picks = new List<Pick>();
        int remaining = Math.Max(0, requiredQty);

        var candidates = (lots ?? Array.Empty<SemiLot>())
            .Where(l => string.Equals(l.SemiKind, semiKind, StringComparison.OrdinalIgnoreCase))
            .Where(l => specRevisionId is null || l.SpecRevisionId == specRevisionId)
            .Where(l => !string.Equals(l.Status, nameof(SemiLotStatus.EXPIRED), StringComparison.Ordinal))
            .Where(l => l.QtyAvailable > 0)
            // FEFO: hết hạn sớm trước (null ExpiryAt = coi như xa nhất), rồi cũ trước.
            .OrderBy(l => l.ExpiryAt ?? DateTime.MaxValue)
            .ThenBy(l => l.CreatedAt)
            .ThenBy(l => l.Id);

        foreach (var lot in candidates)
        {
            if (remaining <= 0) break;
            int take = Math.Min(lot.QtyAvailable, remaining);
            if (take <= 0) continue;
            picks.Add(new Pick(lot.Id, lot.LotNo, take));
            remaining -= take;
        }

        int allocated = requiredQty - remaining;
        return new Plan(picks, allocated, remaining);
    }

    /// <summary>
    /// Assembly leg (InputSource=FROM_STOCK|MIXED) đã đủ input semi từ kho
    /// chưa: tổng <see cref="SemiAllocation.QtyReserved"/> (của leg) +
    /// phần in-line (<paramref name="inLineDoneQty"/>, cho MIXED) ≥
    /// <paramref name="requiredQty"/>.
    /// </summary>
    public static bool IsSatisfied(IEnumerable<SemiAllocation> allocations, long assemblyLegId, int requiredQty, int inLineDoneQty = 0)
    {
        int reserved = (allocations ?? Array.Empty<SemiAllocation>())
            .Where(a => a.AssemblyLegId == assemblyLegId)
            .Sum(a => a.QtyReserved);
        return reserved + inLineDoneQty >= requiredQty;
    }
}
