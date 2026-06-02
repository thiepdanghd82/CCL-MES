using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 — SpecHub 9-state Shop Order badge derivation.
/// Pure helper; mocks WorkOrder + LastQc(IPQC) in-memory. Lock in the
/// terminal-state precedence (Cancelled / Closed / Finished / OnHold
/// override step semantics) + the 3-way IPQC branch (wait / ok / fail).
/// </summary>
public class WorkOrderStatusBadgeTests
{
    [Fact]
    public void Cancelled_status_overrides_step_and_yields_cancelled_badge()
    {
        var wo = new WorkOrder
        {
            Status      = WoStatus.Cancelled,
            CurrentStep = ProcessStepCode.Running,    // ignored when Cancelled
        };
        var b = WorkOrderStatusBadge.From(wo);
        Assert.Equal("cancelled", b.Token);
        Assert.Equal("shop-pill-cancelled", b.CssClass);
    }

    [Theory]
    [InlineData(WoStatus.Closed)]
    [InlineData(WoStatus.Finished)]
    public void Terminal_done_states_yield_done_badge(WoStatus terminal)
    {
        var wo = new WorkOrder { Status = terminal, CurrentStep = ProcessStepCode.Running };
        var b = WorkOrderStatusBadge.From(wo);
        Assert.Equal("done", b.Token);
    }

    [Fact]
    public void OnHold_yields_paused_badge_regardless_of_step()
    {
        var wo = new WorkOrder { Status = WoStatus.OnHold, CurrentStep = ProcessStepCode.Running };
        var b = WorkOrderStatusBadge.From(wo);
        Assert.Equal("paused", b.Token);
    }

    [Fact]
    public void Draft_at_PrePressCheck_yields_new_badge()
    {
        var wo = new WorkOrder { Status = WoStatus.Draft, CurrentStep = ProcessStepCode.PrePressCheck };
        var b = WorkOrderStatusBadge.From(wo);
        Assert.Equal("new", b.Token);
    }

    [Fact]
    public void Non_draft_at_PrePressCheck_yields_pre_press_badge()
    {
        var wo = new WorkOrder { Status = WoStatus.InProgress, CurrentStep = ProcessStepCode.PrePressCheck };
        var b = WorkOrderStatusBadge.From(wo);
        Assert.Equal("pre_press", b.Token);
    }

    [Fact]
    public void OpSetting_yields_setting_badge()
    {
        var wo = new WorkOrder { Status = WoStatus.InProgress, CurrentStep = ProcessStepCode.OpSetting };
        Assert.Equal("setting", WorkOrderStatusBadge.From(wo).Token);
    }

    [Fact]
    public void IpqcApproval_with_no_ipqc_recorded_yields_ipqc_wait()
    {
        var wo = new WorkOrder { Status = WoStatus.InProgress, CurrentStep = ProcessStepCode.IpqcApproval };
        Assert.Equal("ipqc_wait", WorkOrderStatusBadge.From(wo).Token);
    }

    [Fact]
    public void IpqcApproval_with_pending_ipqc_yields_ipqc_wait()
    {
        var wo = WoWithLastIpqc(ProcessStepCode.IpqcApproval, QcResult.Pending);
        Assert.Equal("ipqc_wait", WorkOrderStatusBadge.From(wo).Token);
    }

    [Fact]
    public void IpqcApproval_with_pass_ipqc_yields_ipqc_approved()
    {
        var wo = WoWithLastIpqc(ProcessStepCode.IpqcApproval, QcResult.Pass);
        Assert.Equal("ipqc_approved", WorkOrderStatusBadge.From(wo).Token);
    }

    [Fact]
    public void IpqcApproval_with_fail_ipqc_yields_ipqc_rejected()
    {
        var wo = WoWithLastIpqc(ProcessStepCode.IpqcApproval, QcResult.Fail);
        Assert.Equal("ipqc_rejected", WorkOrderStatusBadge.From(wo).Token);
    }

    [Fact]
    public void ReadyToRun_yields_ready_badge()
    {
        var wo = new WorkOrder { Status = WoStatus.InProgress, CurrentStep = ProcessStepCode.ReadyToRun };
        Assert.Equal("ready", WorkOrderStatusBadge.From(wo).Token);
    }

    [Fact]
    public void Running_yields_running_badge()
    {
        var wo = new WorkOrder { Status = WoStatus.InProgress, CurrentStep = ProcessStepCode.Running };
        Assert.Equal("running", WorkOrderStatusBadge.From(wo).Token);
    }

    [Theory]
    [InlineData(ProcessStepCode.Fqc)]
    [InlineData(ProcessStepCode.Oqc)]
    public void Fqc_and_Oqc_both_yield_qa_pending_badge(ProcessStepCode step)
    {
        var wo = new WorkOrder { Status = WoStatus.InProgress, CurrentStep = step };
        Assert.Equal("qa_pending", WorkOrderStatusBadge.From(wo).Token);
    }

    [Fact]
    public void Closed_step_yields_done_badge_even_without_terminal_status()
    {
        var wo = new WorkOrder { Status = WoStatus.InProgress, CurrentStep = ProcessStepCode.Closed };
        Assert.Equal("done", WorkOrderStatusBadge.From(wo).Token);
    }

    private static WorkOrder WoWithLastIpqc(ProcessStepCode step, QcResult result) =>
        new()
        {
            Status      = WoStatus.InProgress,
            CurrentStep = step,
            Inspections = new List<QcInspection>
            {
                new() { Id = 1, Type = QcType.IPQC, Result = result },
            },
        };
}
