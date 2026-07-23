using CCL.MES.Application.Services;
using CCL.MES.Domain.Routing;
using Xunit;
using Op = CCL.MES.Domain.Routing.RoutingLegResolver.RoutingOp;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P11-1 — resolver routing → leg DAG. Dùng ĐÚNG bộ luật production
/// (<see cref="RoutingLegMapSeed"/>) để test khớp seed. Phủ 3 topology
/// T1/T2/T3 + op không map.
/// </summary>
public sealed class RoutingLegResolverTests
{
    // Chiếu seed (Application) → MapEntry (Domain) — test tham chiếu cả 2 tầng.
    private static IReadOnlyList<RoutingLegResolver.MapEntry> Map() =>
        RoutingLegMapSeed.DefaultEntries()
            .Select(e => new RoutingLegResolver.MapEntry(e.MatchType, e.MatchValue, e.LegKind, e.Method, e.ProcessLine, e.Sort))
            .ToList();

    [Fact]
    public void T1_combined_inline_yields_single_printcut_leg_no_edges()
    {
        var ops = new[] { new Op("10", "IN BẾ Flexo inline", null, null) };

        var plan = RoutingLegResolver.Resolve(ops, Map());

        Assert.Empty(plan.Unmapped);
        Assert.Single(plan.Legs);
        Assert.Equal(nameof(LegKind.PRINT_CUT), plan.Legs[0].LegKind);
        Assert.Empty(plan.Edges);
    }

    [Fact]
    public void T2_print_then_cut_yields_two_legs_one_soft_edge()
    {
        var ops = new[]
        {
            new Op("10", "HP Indigo print", "IDG01", "HP Indigo"),
            new Op("20", "Die cut", "RDC01", "Rotary die cut"),
        };

        var plan = RoutingLegResolver.Resolve(ops, Map());

        Assert.Empty(plan.Unmapped);
        Assert.Equal(2, plan.Legs.Count);
        Assert.Equal(nameof(LegKind.PRINT), plan.Legs[0].LegKind);
        Assert.Equal("DIGITAL", plan.Legs[0].ProcessLine);
        Assert.Equal(nameof(LegKind.CUT), plan.Legs[1].LegKind);
        var edge = Assert.Single(plan.Edges);
        Assert.Equal(0, edge.FromSeq);
        Assert.Equal(1, edge.ToSeq);
        Assert.Equal(DependencyGate.SOFT, edge.Gate);
    }

    [Fact]
    public void T3_silkscreen_tape_assembly_yields_fork_join_dag()
    {
        var ops = new[]
        {
            new Op("10", "Silkscreen print", "MSS01", "SS(Sheet)"),  // PRINT/SILK
            new Op("20", "CẮT TAPE", null, null),                     // TAPE (OpKeyword)
            new Op("30", "DÁN TAPE với semi-in", null, null),         // ASSEMBLY
            new Op("40", "CẮT OUTLINE", null, null),                  // CUT
        };

        var plan = RoutingLegResolver.Resolve(ops, Map());

        Assert.Empty(plan.Unmapped);
        Assert.Equal(4, plan.Legs.Count);
        Assert.Equal(nameof(LegKind.PRINT),    plan.Legs[0].LegKind);
        Assert.Equal(nameof(LegKind.TAPE),     plan.Legs[1].LegKind);
        Assert.Equal(nameof(LegKind.ASSEMBLY), plan.Legs[2].LegKind);
        Assert.Equal(nameof(LegKind.CUT),      plan.Legs[3].LegKind);

        // 2 cạnh HARD vào ASSEMBLY (từ PRINT + TAPE) + 1 cạnh SOFT ASSEMBLY→CUT.
        Assert.Equal(3, plan.Edges.Count);
        Assert.Contains(plan.Edges, e => e.FromSeq == 0 && e.ToSeq == 2 && e.Gate == DependencyGate.HARD);
        Assert.Contains(plan.Edges, e => e.FromSeq == 1 && e.ToSeq == 2 && e.Gate == DependencyGate.HARD);
        Assert.Contains(plan.Edges, e => e.FromSeq == 2 && e.ToSeq == 3 && e.Gate == DependencyGate.SOFT);
    }

    [Fact]
    public void Unmapped_op_is_reported_not_guessed()
    {
        var ops = new[]
        {
            new Op("10", "Silkscreen print", "MSS01", "SS(Sheet)"),
            new Op("20", "Công đoạn lạ XYZ", "ZZZ99", "Unknown machine"),
        };

        var plan = RoutingLegResolver.Resolve(ops, Map());

        Assert.Single(plan.Legs);              // chỉ op in map được thành leg
        var sig = Assert.Single(plan.Unmapped);
        Assert.Contains("ZZZ99", sig);
    }

    [Fact]
    public void Empty_map_reports_everything_unmapped()
    {
        var ops = new[] { new Op("10", "Silkscreen", "MSS01", null) };
        var plan = RoutingLegResolver.Resolve(ops, Array.Empty<RoutingLegResolver.MapEntry>());
        Assert.Empty(plan.Legs);
        Assert.Single(plan.Unmapped);
    }
}
