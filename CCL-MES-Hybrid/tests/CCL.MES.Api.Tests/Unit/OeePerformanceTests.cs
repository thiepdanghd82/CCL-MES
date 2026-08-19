using CCL.MES.Api.Services;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Đợt 1 C3 — the OEE performance leg, unit level.
///
/// Two things are pinned here. First the formula: ideal cycle is DERIVED
/// from WorkCenter.IdealSpeedPcsH as 3600/speed, matching
/// ShopOrdersController.cs; Machine.IdealCycleTimeSec is not an input to
/// this function and gate-oee-single-source.sh keeps it that way.
///
/// Second, and this is the actual bug being fixed: a null performance must
/// always arrive with a reason. 19 of 27 WOs lost the metric silently
/// because null meant both "not applicable" and "we have no idea". The
/// invariant test at the bottom is the one that must never go red.
/// </summary>
public sealed class OeePerformanceTests
{
    // 600 pcs/h → ideal cycle 6s. 3600s of runtime → 600 planned units.
    // 300 produced → exactly 50%.
    [Fact]
    public void Computes_performance_from_workcenter_speed()
    {
        var r = OeePerformance.Compute(
            workCenterResolved: true, idealSpeedPcsH: 600, runSeconds: 3600, qtyDone: 300);

        Assert.NotNull(r.Performance);
        Assert.Equal(0.5, r.Performance!.Value, precision: 9);
        Assert.Null(r.UnavailableReason);
    }

    // Same numbers as the canonical ShopOrdersController path: speed →
    // 3600/speed → plannedUnits. Guards against somebody "simplifying" the
    // formula into seconds-per-unit again.
    [Theory]
    [InlineData(3600.0, 3600, 3600, 1.0)]   // 1 pcs/s, ran an hour, made 3600
    [InlineData(1200.0, 1800, 600, 1.0)]    // 1200 pcs/h for 30 min = 600 planned
    [InlineData(1200.0, 1800, 300, 0.5)]
    public void Formula_matches_3600_over_speed(
        double speed, long runSeconds, int qtyDone, double expected)
    {
        var r = OeePerformance.Compute(true, speed, runSeconds, qtyDone);
        Assert.Equal(expected, r.Performance!.Value, precision: 9);
    }

    [Fact]
    public void No_work_center_reports_workcenter_missing()
    {
        var r = OeePerformance.Compute(
            workCenterResolved: false, idealSpeedPcsH: null, runSeconds: 3600, qtyDone: 300);

        Assert.Null(r.Performance);
        Assert.Equal(OeePerformance.ReasonWorkCenterMissing, r.UnavailableReason);
    }

    // The common case in production today: 38 of 43 work centers have no
    // speed. This must be distinguishable from "no work center at all",
    // because the fix is different (fill the speed vs. assign the machine).
    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void Missing_or_nonpositive_speed_reports_speed_missing(double? speed)
    {
        var r = OeePerformance.Compute(true, speed, runSeconds: 3600, qtyDone: 300);

        Assert.Null(r.Performance);
        Assert.Equal(OeePerformance.ReasonWorkCenterSpeedMissing, r.UnavailableReason);
    }

    [Fact]
    public void Zero_runtime_reports_no_runtime()
    {
        var r = OeePerformance.Compute(true, 600, runSeconds: 0, qtyDone: 0);

        Assert.Null(r.Performance);
        Assert.Equal(OeePerformance.ReasonNoRuntime, r.UnavailableReason);
    }

    /// <summary>
    /// THE invariant. Exactly one of (Performance, UnavailableReason) is
    /// non-null, for every input combination. A silent null is the bug.
    /// </summary>
    [Fact]
    public void Never_returns_a_null_performance_without_a_reason()
    {
        bool[] resolved = { true, false };
        double?[] speeds = { null, -1, 0, 0.5, 600, 100000 };
        long[] runtimes = { -1, 0, 1, 3600 };
        int[] quantities = { 0, 1, 300 };

        foreach (var wc in resolved)
        foreach (var s in speeds)
        foreach (var rt in runtimes)
        foreach (var q in quantities)
        {
            var r = OeePerformance.Compute(wc, s, rt, q);
            var hasValue = r.Performance is not null;
            var hasReason = r.UnavailableReason is not null;
            Assert.True(hasValue ^ hasReason,
                $"wc={wc} speed={s} run={rt} qty={q} → perf={r.Performance?.ToString() ?? "null"} " +
                $"reason={r.UnavailableReason ?? "null"} — exactly one must be set.");
        }
    }
}
