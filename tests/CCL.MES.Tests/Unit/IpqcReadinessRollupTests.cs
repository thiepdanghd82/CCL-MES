using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7d-1 — pure-helper coverage for the IPQC review readiness gate.
/// Mirrors the 7b <see cref="MaterialsReadinessRollupTests"/> shape so
/// future test surfaces follow the same convention.
///
/// Contract:
///   - Null check row → all flags false (lazy-materialise case).
///   - All 4 slots Pending → IsReadyForJudgment false (can't judge yet).
///   - All 4 slots non-Pending → IsReadyForJudgment true.
///   - AllOk requires all 4 = Ok.
///   - AnyNg true iff at least one = Ng.
///   - IsJudgmentConsistent rejects GoRun when any slot is NG, rejects
///     SpecialAccept when no slot is NG, accepts StopLine always (when
///     ready), rejects Pending always.
/// </summary>
public sealed class IpqcReadinessRollupTests
{
    private static WoIpqcCheck Check(
        IpqcCheckStatus mat = IpqcCheckStatus.Pending,
        IpqcCheckStatus a = IpqcCheckStatus.Pending,
        IpqcCheckStatus b = IpqcCheckStatus.Pending,
        IpqcCheckStatus c = IpqcCheckStatus.Pending) =>
        new()
        {
            WorkOrderId = 1,
            MaterialStatus = mat,
            PrintAStatus = a,
            PrintBStatus = b,
            PrintCStatus = c,
        };

    // ── Null + all-Pending → not ready ─────────────────────────────

    [Fact]
    public void Null_check_returns_all_false()
    {
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(null);
        Assert.False(ready);
        Assert.False(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void All_four_slots_Pending_returns_not_ready()
    {
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(Check());
        Assert.False(ready);
        Assert.False(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void Three_slots_Ok_one_Pending_still_not_ready()
    {
        var check = Check(
            mat: IpqcCheckStatus.Ok,
            a: IpqcCheckStatus.Ok,
            b: IpqcCheckStatus.Ok,
            c: IpqcCheckStatus.Pending); // ← bites
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check);
        Assert.False(ready);
        Assert.False(allOk);
        Assert.False(anyNg);
    }

    // ── Ready states ───────────────────────────────────────────────

    [Fact]
    public void All_four_slots_Ok_ready_and_AllOk()
    {
        var check = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ok,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check);
        Assert.True(ready);
        Assert.True(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void Three_Ok_one_Ng_ready_and_AnyNg()
    {
        var check = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ok,
                          IpqcCheckStatus.Ng, IpqcCheckStatus.Ok);
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check);
        Assert.True(ready);
        Assert.False(allOk);
        Assert.True(anyNg);
    }

    [Fact]
    public void All_four_Ng_ready_and_AnyNg_not_AllOk()
    {
        var check = Check(IpqcCheckStatus.Ng, IpqcCheckStatus.Ng,
                          IpqcCheckStatus.Ng, IpqcCheckStatus.Ng);
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check);
        Assert.True(ready);
        Assert.False(allOk);
        Assert.True(anyNg);
    }

    // ── Judgment consistency ───────────────────────────────────────

    [Fact]
    public void GoRun_only_consistent_when_AllOk()
    {
        var allOk = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ok,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        Assert.True(IpqcReadinessRollup.IsJudgmentConsistent(allOk, IpqcJudgment.GoRun));

        var oneNg = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ng,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        Assert.False(IpqcReadinessRollup.IsJudgmentConsistent(oneNg, IpqcJudgment.GoRun));
    }

    [Fact]
    public void StopLine_consistent_when_ready_regardless_of_OK_NG()
    {
        var allOk = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ok,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        var oneNg = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ng,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        Assert.True(IpqcReadinessRollup.IsJudgmentConsistent(allOk, IpqcJudgment.StopLine));
        Assert.True(IpqcReadinessRollup.IsJudgmentConsistent(oneNg, IpqcJudgment.StopLine));
    }

    [Fact]
    public void SpecialAccept_only_consistent_when_AnyNg()
    {
        var allOk = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ok,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        Assert.False(IpqcReadinessRollup.IsJudgmentConsistent(allOk, IpqcJudgment.SpecialAccept));

        var oneNg = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ng,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        Assert.True(IpqcReadinessRollup.IsJudgmentConsistent(oneNg, IpqcJudgment.SpecialAccept));
    }

    [Fact]
    public void Pending_judgment_never_consistent()
    {
        var allOk = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ok,
                          IpqcCheckStatus.Ok, IpqcCheckStatus.Ok);
        Assert.False(IpqcReadinessRollup.IsJudgmentConsistent(allOk, IpqcJudgment.Pending));
    }

    [Fact]
    public void Not_ready_judgment_never_consistent()
    {
        var notReady = Check(IpqcCheckStatus.Ok, IpqcCheckStatus.Ok,
                             IpqcCheckStatus.Ok, IpqcCheckStatus.Pending);
        Assert.False(IpqcReadinessRollup.IsJudgmentConsistent(notReady, IpqcJudgment.GoRun));
        Assert.False(IpqcReadinessRollup.IsJudgmentConsistent(notReady, IpqcJudgment.StopLine));
        Assert.False(IpqcReadinessRollup.IsJudgmentConsistent(notReady, IpqcJudgment.SpecialAccept));
    }
}
