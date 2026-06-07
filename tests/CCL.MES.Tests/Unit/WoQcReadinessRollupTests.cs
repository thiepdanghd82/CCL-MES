using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7e-1 Q3+Q7 — pure-helper coverage for the data-driven
/// FQC/OQC readiness gate. Mirrors the 7d
/// <see cref="IpqcReadinessRollupTests"/> shape but exercises the
/// CHILD-ROW model (snapshot-driven items list) rather than the
/// hardcoded 4 slots.
///
/// Contract — Compute returns (IsReadyForJudgment, AllOk, AnyNg):
///   - Null check row → all flags false (lazy-materialise case).
///   - Empty items list → all flags false (operators see "not
///     ready" rather than a silent auto-pass on an empty profile
///     config; admins notice missing snapshot config).
///   - All items Pending → IsReadyForJudgment false.
///   - All items non-Pending → IsReadyForJudgment true.
///   - AllOk requires every item = Ok.
///   - AnyNg true iff at least one item = Ng.
///
/// IsJudgmentConsistent — both Pass and Reject are valid for FQC + OQC
/// when ready (no GoRun/StopLine analogue — sample-based AQL allows
/// minor NGs to coexist with Pass).
/// </summary>
public sealed class WoQcReadinessRollupTests
{
    private static WoQcCheck Check(string kind, params IpqcCheckStatus[] itemStatuses)
    {
        var check = new WoQcCheck
        {
            WorkOrderId = 1,
            QcKind = kind,
            ProfileSnapshotJson = "{}",
        };
        for (var i = 0; i < itemStatuses.Length; i++)
        {
            check.Items.Add(new WoQcCheckItem
            {
                WoQcCheckId = 0,
                ItemKey = $"item-{i}",
                Status = itemStatuses[i],
            });
        }
        return check;
    }

    [Fact]
    public void Null_check_returns_all_false()
    {
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(null);
        Assert.False(ready);
        Assert.False(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void Empty_items_list_returns_all_false()
    {
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(Check("FQC"));
        Assert.False(ready);
        Assert.False(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void All_items_pending_returns_not_ready()
    {
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(
            Check("FQC",
                IpqcCheckStatus.Pending,
                IpqcCheckStatus.Pending,
                IpqcCheckStatus.Pending));
        Assert.False(ready);
        Assert.False(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void All_items_ok_returns_ready_and_allOk_and_no_NG()
    {
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(
            Check("FQC",
                IpqcCheckStatus.Ok,
                IpqcCheckStatus.Ok,
                IpqcCheckStatus.Ok));
        Assert.True(ready);
        Assert.True(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void Mixed_ok_and_ng_returns_ready_and_anyNg_but_not_allOk()
    {
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(
            Check("OQC",
                IpqcCheckStatus.Ok,
                IpqcCheckStatus.Ng,
                IpqcCheckStatus.Ok));
        Assert.True(ready);
        Assert.False(allOk);
        Assert.True(anyNg);
    }

    [Fact]
    public void All_items_ng_returns_ready_and_anyNg()
    {
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(
            Check("FQC",
                IpqcCheckStatus.Ng,
                IpqcCheckStatus.Ng,
                IpqcCheckStatus.Ng));
        Assert.True(ready);
        Assert.False(allOk);
        Assert.True(anyNg);
    }

    [Fact]
    public void Pending_mixed_with_ok_still_not_ready()
    {
        // Q3 invariant: every item MUST be resolved before judgment
        // enables. One Pending blocks the gate even when the others
        // are decisive.
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(
            Check("OQC",
                IpqcCheckStatus.Ok,
                IpqcCheckStatus.Pending,
                IpqcCheckStatus.Ok));
        Assert.False(ready);
        Assert.False(allOk);
        Assert.False(anyNg);
    }

    [Fact]
    public void IsJudgmentConsistent_null_check_returns_false_for_all_judgments()
    {
        Assert.False(WoQcReadinessRollup.IsJudgmentConsistent(null, WoQcJudgment.Pass));
        Assert.False(WoQcReadinessRollup.IsJudgmentConsistent(null, WoQcJudgment.Reject));
        Assert.False(WoQcReadinessRollup.IsJudgmentConsistent(null, WoQcJudgment.Pending));
    }

    [Fact]
    public void IsJudgmentConsistent_not_ready_returns_false()
    {
        var check = Check("FQC", IpqcCheckStatus.Ok, IpqcCheckStatus.Pending);
        Assert.False(WoQcReadinessRollup.IsJudgmentConsistent(check, WoQcJudgment.Pass));
        Assert.False(WoQcReadinessRollup.IsJudgmentConsistent(check, WoQcJudgment.Reject));
    }

    [Fact]
    public void IsJudgmentConsistent_ready_accepts_Pass_and_Reject_rejects_Pending()
    {
        // Even all-Ng FQC accepts Pass — operator + reviewer decide
        // per AQL sample bucket; the rollup helper doesn't gate.
        var check = Check("FQC", IpqcCheckStatus.Ng, IpqcCheckStatus.Ng);
        Assert.True(WoQcReadinessRollup.IsJudgmentConsistent(check, WoQcJudgment.Pass));
        Assert.True(WoQcReadinessRollup.IsJudgmentConsistent(check, WoQcJudgment.Reject));
        Assert.False(WoQcReadinessRollup.IsJudgmentConsistent(check, WoQcJudgment.Pending));
    }
}
