using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>P11.5 — planner FEFO + gate "assembly đủ input từ kho" (pure).</summary>
public sealed class SemiStockAllocatorTests
{
    private static SemiLot Lot(long id, string kind, int avail, DateTime? expiry, DateTime created, long? spec = 7, string status = "AVAILABLE") => new()
    {
        Id = id, LotNo = $"LOT{id}", SemiKind = kind, QtyAvailable = avail,
        ExpiryAt = expiry, CreatedAt = created, SpecRevisionId = spec, Status = status,
    };

    private static readonly DateTime T0 = new(2026, 7, 1);

    [Fact]
    public void Fefo_picks_earliest_expiry_first()
    {
        var lots = new[]
        {
            Lot(1, "PRINTED_SEMI", 400, T0.AddDays(30), T0),          // hết hạn muộn
            Lot(2, "PRINTED_SEMI", 400, T0.AddDays(5),  T0.AddDays(1)), // hết hạn SỚM → ưu tiên
        };
        var plan = SemiStockAllocator.Fefo(lots, "PRINTED_SEMI", 7, 500);

        Assert.Equal(0, plan.Shortfall);
        Assert.Equal(500, plan.Allocated);
        Assert.Equal(2L, plan.Picks[0].SemiLotId);   // lô hết hạn sớm trước
        Assert.Equal(400, plan.Picks[0].Qty);
        Assert.Equal(1L, plan.Picks[1].SemiLotId);
        Assert.Equal(100, plan.Picks[1].Qty);
    }

    [Fact]
    public void Fefo_filters_kind_and_spec()
    {
        var lots = new[]
        {
            Lot(1, "TAPE_SEMI",    500, null, T0),           // sai kind
            Lot(2, "PRINTED_SEMI", 500, null, T0, spec: 99), // sai spec
            Lot(3, "PRINTED_SEMI", 500, null, T0, spec: 7),  // đúng
        };
        var plan = SemiStockAllocator.Fefo(lots, "PRINTED_SEMI", 7, 300);
        Assert.Equal(0, plan.Shortfall);
        var pick = Assert.Single(plan.Picks);
        Assert.Equal(3L, pick.SemiLotId);
    }

    [Fact]
    public void Fefo_skips_expired_and_empty_lots()
    {
        var lots = new[]
        {
            Lot(1, "PRINTED_SEMI", 500, T0.AddDays(-1), T0, status: "EXPIRED"), // hết hạn
            Lot(2, "PRINTED_SEMI", 0,   null, T0),                              // hết hàng
            Lot(3, "PRINTED_SEMI", 200, null, T0),                              // dùng được
        };
        var plan = SemiStockAllocator.Fefo(lots, "PRINTED_SEMI", 7, 200);
        var pick = Assert.Single(plan.Picks);
        Assert.Equal(3L, pick.SemiLotId);
        Assert.Equal(0, plan.Shortfall);
    }

    [Fact]
    public void Fefo_reports_shortfall_when_insufficient()
    {
        var lots = new[] { Lot(1, "PRINTED_SEMI", 100, null, T0) };
        var plan = SemiStockAllocator.Fefo(lots, "PRINTED_SEMI", 7, 500);
        Assert.Equal(100, plan.Allocated);
        Assert.Equal(400, plan.Shortfall);   // caller từ chối reserve
    }

    [Fact]
    public void IsSatisfied_true_when_reserved_meets_required()
    {
        var allocs = new[]
        {
            new SemiAllocation { AssemblyLegId = 10, QtyReserved = 600 },
            new SemiAllocation { AssemblyLegId = 10, QtyReserved = 400 },
            new SemiAllocation { AssemblyLegId = 99, QtyReserved = 999 }, // leg khác — bỏ
        };
        Assert.True(SemiStockAllocator.IsSatisfied(allocs, 10, 1000));
        Assert.False(SemiStockAllocator.IsSatisfied(allocs, 10, 1001));
    }

    [Fact]
    public void IsSatisfied_mixed_counts_inline_done_qty()
    {
        var allocs = new[] { new SemiAllocation { AssemblyLegId = 10, QtyReserved = 300 } };
        // MIXED: 300 từ kho + 700 in-line = 1000 ≥ 1000.
        Assert.True(SemiStockAllocator.IsSatisfied(allocs, 10, 1000, inLineDoneQty: 700));
        Assert.False(SemiStockAllocator.IsSatisfied(allocs, 10, 1000, inLineDoneQty: 699));
    }
}
