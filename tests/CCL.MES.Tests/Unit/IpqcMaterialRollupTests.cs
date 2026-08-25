using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// IPQC first-article — MATERIAL (SYSTEM) readiness rollup coverage
/// (Henry 2026-08-25, Q1 soft-lock). "Resolved" = OK, or NG-with-Engineer-
/// approved-waiver. Empty set → AllResolved (legacy parity: a WO with no BOM
/// material never blocks judgment).
/// </summary>
public sealed class IpqcMaterialRollupTests
{
    private static WoIpqcMaterialCheck Row(
        IpqcCheckStatus status = IpqcCheckStatus.Pending,
        DivergenceApprovalStatus approval = DivergenceApprovalStatus.NotRequired) =>
        new() { WorkOrderId = 1, Status = status, DivergenceApprovalStatus = approval };

    // ── LegacyParity: empty / null never blocks ────────────────────────

    [Fact]
    public void Empty_set_is_all_resolved()
    {
        var (all, pending, rejected) = IpqcMaterialRollup.Compute(new List<WoIpqcMaterialCheck>());
        Assert.True(all);
        Assert.False(pending);
        Assert.False(rejected);
    }

    [Fact]
    public void Null_set_is_all_resolved()
    {
        var (all, _, _) = IpqcMaterialRollup.Compute(null);
        Assert.True(all);
    }

    // ── Resolved definitions ───────────────────────────────────────────

    [Fact]
    public void All_ok_is_resolved()
    {
        var rows = new[] { Row(IpqcCheckStatus.Ok), Row(IpqcCheckStatus.Ok) };
        var (all, pending, rejected) = IpqcMaterialRollup.Compute(rows);
        Assert.True(all);
        Assert.False(pending);
        Assert.False(rejected);
    }

    [Fact]
    public void Ng_with_approved_waiver_is_resolved()
    {
        var rows = new[]
        {
            Row(IpqcCheckStatus.Ok),
            Row(IpqcCheckStatus.Ng, DivergenceApprovalStatus.Approved),
        };
        var (all, pending, rejected) = IpqcMaterialRollup.Compute(rows);
        Assert.True(all);
        Assert.False(pending);
        Assert.False(rejected);
    }

    [Fact]
    public void Ng_pending_engineer_is_not_resolved()
    {
        var rows = new[]
        {
            Row(IpqcCheckStatus.Ok),
            Row(IpqcCheckStatus.Ng, DivergenceApprovalStatus.PendingEngineer),
        };
        var (all, pending, rejected) = IpqcMaterialRollup.Compute(rows);
        Assert.False(all);
        Assert.True(pending);
        Assert.False(rejected);
    }

    [Fact]
    public void Ng_rejected_waiver_is_not_resolved()
    {
        var rows = new[] { Row(IpqcCheckStatus.Ng, DivergenceApprovalStatus.Rejected) };
        var (all, pending, rejected) = IpqcMaterialRollup.Compute(rows);
        Assert.False(all);
        Assert.False(pending);
        Assert.True(rejected);
    }

    [Fact]
    public void Ng_without_waiver_is_not_resolved()
    {
        // Plain NG (no divergence, no waiver) must NOT count as resolved — a
        // genuine material NG has to StopLine, not slip through as ready.
        var rows = new[] { Row(IpqcCheckStatus.Ng) };
        var (all, _, _) = IpqcMaterialRollup.Compute(rows);
        Assert.False(all);
    }

    [Fact]
    public void Pending_confirm_is_not_resolved()
    {
        var rows = new[] { Row(IpqcCheckStatus.Pending) };
        var (all, _, _) = IpqcMaterialRollup.Compute(rows);
        Assert.False(all);
    }

    [Fact]
    public void Mixed_ok_and_pending_waiver_flags_pending_and_not_resolved()
    {
        var rows = new[]
        {
            Row(IpqcCheckStatus.Ok),
            Row(IpqcCheckStatus.Ng, DivergenceApprovalStatus.Approved),
            Row(IpqcCheckStatus.Ng, DivergenceApprovalStatus.PendingEngineer),
        };
        var (all, pending, rejected) = IpqcMaterialRollup.Compute(rows);
        Assert.False(all);
        Assert.True(pending);
        Assert.False(rejected);
    }
}
