using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 — Lock-in regression for the 7-step Work Order flow.
/// Covers every (currentStep → nextStep) edge in <see cref="WorkOrderStateMachine.CanAdvance"/>
/// plus every <see cref="WoErrorCode"/> the guards can emit.
///
/// Strict pure-unit test: no EF, no DI, no IO. Each test builds a minimal
/// WorkOrder + (optional) QcInspection list in-memory and asserts the
/// TransitionResult tuple.
/// </summary>
public class WorkOrderStateMachineTests
{
    // ── 7 HAPPY transitions ────────────────────────────────────────────

    [Fact]
    public void Happy_PrePressCheck_To_OpSetting_when_spec_and_materials_ready()
    {
        var wo = new WorkOrder
        {
            CurrentStep      = ProcessStepCode.PrePressCheck,
            ProductRevisionId = 1,
            MaterialsReady   = true,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Happy_OpSetting_To_IpqcApproval_when_setup_confirmed()
    {
        var wo = new WorkOrder
        {
            CurrentStep    = ProcessStepCode.OpSetting,
            SetupConfirmed = true,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Happy_IpqcApproval_To_ReadyToRun_when_last_IPQC_pass()
    {
        var wo = WoWithLastQc(ProcessStepCode.IpqcApproval, QcType.IPQC, QcResult.Pass);
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Happy_ReadyToRun_To_Running_unconditional()
    {
        var wo = new WorkOrder { CurrentStep = ProcessStepCode.ReadyToRun };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Happy_Running_To_Fqc_when_produced_qty_positive()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.Running,
            ProducedQty = 1,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Happy_Fqc_To_Oqc_when_last_FQC_pass()
    {
        var wo = WoWithLastQc(ProcessStepCode.Fqc, QcType.FQC, QcResult.Pass);
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Happy_Oqc_To_Closed_when_last_OQC_pass_and_rohs_ok()
    {
        var wo = WoWithLastQc(ProcessStepCode.Oqc, QcType.OQC, QcResult.Pass);
        wo.RohsOk = true;
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
    }

    // ── 7 DENY paths — one per WoErrorCode the state machine can emit ─

    [Fact]
    public void Deny_AlreadyAtFinalStep_when_currentStep_is_Closed()
    {
        var wo = new WorkOrder { CurrentStep = ProcessStepCode.Closed };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.AlreadyAtFinalStep, r.Error);
    }

    [Fact]
    public void Deny_RequiresSpecAndMaterials_when_revision_null()
    {
        var wo = new WorkOrder
        {
            CurrentStep      = ProcessStepCode.PrePressCheck,
            ProductRevisionId = null,
            MaterialsReady   = true,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.RequiresSpecAndMaterials, r.Error);
    }

    [Fact]
    public void Deny_RequiresSpecAndMaterials_when_materials_not_ready()
    {
        var wo = new WorkOrder
        {
            CurrentStep      = ProcessStepCode.PrePressCheck,
            ProductRevisionId = 1,
            MaterialsReady   = false,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.RequiresSpecAndMaterials, r.Error);
    }

    [Fact]
    public void Deny_RequiresSetupConfirmed_when_setup_not_confirmed()
    {
        var wo = new WorkOrder
        {
            CurrentStep    = ProcessStepCode.OpSetting,
            SetupConfirmed = false,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.RequiresSetupConfirmed, r.Error);
    }

    [Theory]
    [InlineData(QcResult.Pending)]
    [InlineData(QcResult.Fail)]
    public void Deny_IpqcNotPassed_when_last_IPQC_not_pass(QcResult lastIpqcResult)
    {
        var wo = WoWithLastQc(ProcessStepCode.IpqcApproval, QcType.IPQC, lastIpqcResult);
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.IpqcNotPassed, r.Error);
    }

    [Fact]
    public void Deny_IpqcNotPassed_when_no_IPQC_recorded_yet()
    {
        // Empty inspection list — LastQc(IPQC) returns null → guard fails.
        var wo = new WorkOrder { CurrentStep = ProcessStepCode.IpqcApproval };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.IpqcNotPassed, r.Error);
    }

    [Fact]
    public void Deny_NoProductionYet_when_produced_qty_zero()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.Running,
            ProducedQty = 0,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.NoProductionYet, r.Error);
    }

    [Theory]
    [InlineData(QcResult.Pending)]
    [InlineData(QcResult.Fail)]
    public void Deny_FqcNotPassed_when_last_FQC_not_pass(QcResult lastFqcResult)
    {
        var wo = WoWithLastQc(ProcessStepCode.Fqc, QcType.FQC, lastFqcResult);
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.FqcNotPassed, r.Error);
    }

    [Theory]
    [InlineData(QcResult.Pending, true)]
    [InlineData(QcResult.Fail, true)]
    [InlineData(QcResult.Pass, false)]
    public void Deny_OqcOrRohsNotMet_when_either_oqc_fail_or_rohs_false(QcResult oqcResult, bool rohsOk)
    {
        var wo = WoWithLastQc(ProcessStepCode.Oqc, QcType.OQC, oqcResult);
        wo.RohsOk = rohsOk;
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.OqcOrRohsNotMet, r.Error);
    }

    // ── Edge cases ─────────────────────────────────────────────────────

    [Fact]
    public void Next_returns_null_at_terminal_state()
    {
        Assert.Null(WorkOrderStateMachine.Next(ProcessStepCode.Closed));
    }

    [Theory]
    [InlineData(ProcessStepCode.PrePressCheck, ProcessStepCode.OpSetting)]
    [InlineData(ProcessStepCode.OpSetting,     ProcessStepCode.IpqcApproval)]
    [InlineData(ProcessStepCode.IpqcApproval,  ProcessStepCode.ReadyToRun)]
    [InlineData(ProcessStepCode.ReadyToRun,    ProcessStepCode.Running)]
    [InlineData(ProcessStepCode.Running,       ProcessStepCode.Fqc)]
    [InlineData(ProcessStepCode.Fqc,           ProcessStepCode.Oqc)]
    [InlineData(ProcessStepCode.Oqc,           ProcessStepCode.Closed)]
    public void Next_returns_correct_successor_for_each_step(
        ProcessStepCode current, ProcessStepCode expected)
    {
        Assert.Equal(expected, WorkOrderStateMachine.Next(current));
    }

    [Fact]
    public void Flow_array_lists_all_8_steps_in_order()
    {
        Assert.Equal(8, WorkOrderStateMachine.Flow.Length);
        Assert.Equal(ProcessStepCode.PrePressCheck, WorkOrderStateMachine.Flow[0]);
        Assert.Equal(ProcessStepCode.Closed,        WorkOrderStateMachine.Flow[7]);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static WorkOrder WoWithLastQc(
        ProcessStepCode step, QcType type, QcResult result)
    {
        // LastQc(type) orders by Id desc → highest Id wins. Build a small
        // 2-entry list so we exercise the OrderByDescending path, not just
        // single-row collections.
        return new WorkOrder
        {
            CurrentStep  = step,
            Inspections  = new List<QcInspection>
            {
                new() { Id = 1, Type = type, Result = QcResult.Pending },
                new() { Id = 2, Type = type, Result = result },
            },
        };
    }
}
