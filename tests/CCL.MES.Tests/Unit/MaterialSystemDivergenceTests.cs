using CCL.MES.Domain;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// IPQC first-article — pure-helper coverage for the MATERIAL (SYSTEM) LOT
/// reconciliation (Henry 2026-08-25). Each of the 4 divergence flags is
/// exercised in isolation, plus the fully-matched (None) case and the
/// shadow-FK-null combination (which necessarily also raises IqcNotPass +
/// LotNotReleased because the join yields nulls).
/// </summary>
public sealed class MaterialSystemDivergenceTests
{
    // The "matched" baseline: FK resolved, IQC Pass, code == lot part, Released.
    private static MaterialSystemDivergence.Input Matched() =>
        new(HasShadowFk: true, IqcResult: "Pass", MaterialCode: "M-1",
            LotPartNo: "M-1", LotStatus: "Released");

    [Fact]
    public void Fully_matched_is_not_divergent()
    {
        var r = MaterialSystemDivergence.Compute(Matched());
        Assert.Equal(DivergenceFlags.None, r.Flags);
        Assert.Equal("None", r.Kind);
        Assert.False(r.IsDivergent);
    }

    [Fact]
    public void ShadowFkNull_alone_raises_shadow_plus_iqc_plus_lot()
    {
        // No lot resolved → IqcResult + LotStatus are null → those two flags
        // co-fire; PartNoMismatch does NOT (nothing to compare against).
        var r = MaterialSystemDivergence.Compute(
            new(HasShadowFk: false, IqcResult: null, MaterialCode: "M-1",
                LotPartNo: null, LotStatus: null));
        Assert.True(r.Flags.HasFlag(DivergenceFlags.ShadowFkNull));
        Assert.True(r.Flags.HasFlag(DivergenceFlags.IqcNotPass));
        Assert.True(r.Flags.HasFlag(DivergenceFlags.LotNotReleased));
        Assert.False(r.Flags.HasFlag(DivergenceFlags.PartNoMismatch));
        Assert.True(r.IsDivergent);
    }

    [Fact]
    public void IqcNotPass_alone()
    {
        var r = MaterialSystemDivergence.Compute(Matched() with { IqcResult = "Fail" });
        Assert.Equal(DivergenceFlags.IqcNotPass, r.Flags);
        Assert.True(r.IsDivergent);
    }

    [Fact]
    public void PartNoMismatch_alone()
    {
        var r = MaterialSystemDivergence.Compute(Matched() with { LotPartNo = "M-OTHER" });
        Assert.Equal(DivergenceFlags.PartNoMismatch, r.Flags);
        Assert.True(r.IsDivergent);
    }

    [Fact]
    public void LotNotReleased_alone()
    {
        // Q3: only "Released" is valid — Consumed counts as divergent.
        var r = MaterialSystemDivergence.Compute(Matched() with { LotStatus = "Consumed" });
        Assert.Equal(DivergenceFlags.LotNotReleased, r.Flags);
        Assert.True(r.IsDivergent);
    }

    [Fact]
    public void Multiple_flags_accumulate_in_bitmask()
    {
        var r = MaterialSystemDivergence.Compute(
            Matched() with { IqcResult = "Fail", LotStatus = "Quarantine" });
        Assert.True(r.Flags.HasFlag(DivergenceFlags.IqcNotPass));
        Assert.True(r.Flags.HasFlag(DivergenceFlags.LotNotReleased));
        Assert.Equal(
            DivergenceFlags.IqcNotPass | DivergenceFlags.LotNotReleased, r.Flags);
        Assert.True(r.IsDivergent);
    }

    [Theory]
    [InlineData("pass")]   // OrdinalIgnoreCase
    [InlineData("PASS")]
    public void IqcResult_match_is_case_insensitive(string result)
    {
        var r = MaterialSystemDivergence.Compute(Matched() with { IqcResult = result });
        Assert.False(r.Flags.HasFlag(DivergenceFlags.IqcNotPass));
    }

    [Theory]
    [InlineData("released")]
    [InlineData("RELEASED")]
    public void LotStatus_match_is_case_insensitive(string status)
    {
        var r = MaterialSystemDivergence.Compute(Matched() with { LotStatus = status });
        Assert.False(r.Flags.HasFlag(DivergenceFlags.LotNotReleased));
    }
}
