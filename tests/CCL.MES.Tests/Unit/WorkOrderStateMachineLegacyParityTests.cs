using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7a-1 legacy parity sweep. These 8 tests pin the EXACT behavior of
/// <see cref="WorkOrderStateMachine.CanAdvance(WorkOrder)"/> as it exists
/// when this file is committed (commit 1 of PR 7a-1.1, before any
/// canonical-state extension code lands).
///
/// Henry-approved exception: extending the legacy state-machine file in
/// place was granted as a one-time exception (see breakdown §1) because
/// drift between two parallel state machines is the larger risk. The
/// exception's conditions are:
///   (a) only ADD — no edit/delete of any existing method line
///   (b) pin behavior with parity tests BEFORE extension code lands
///   (c) parity sweep runs in every PR of the 7a-1 stack's verify script
///
/// This file is condition (b). Filter-run via:
///   dotnet test --filter "Category=LegacyParity"
///
/// If any test in this file fails after the canonical extension lands,
/// the extension violated condition (a) — investigate immediately.
/// Do NOT relax these tests to make them pass — fix the extension to
/// preserve legacy behavior.
/// </summary>
[Trait("Category", "LegacyParity")]
public sealed class WorkOrderStateMachineLegacyParityTests
{
    [Fact]
    public void Parity_01_PrePressCheck_advances_to_OpSetting_when_spec_and_materials_ready()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.PrePressCheck,
            ProductRevisionId = 1,
            MaterialsReady = true,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parity_02_OpSetting_advances_to_IpqcApproval_when_setup_confirmed()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.OpSetting,
            SetupConfirmed = true,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parity_03_IpqcApproval_advances_to_ReadyToRun_when_last_IPQC_pass()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.IpqcApproval,
            Inspections = new List<QcInspection>
            {
                new() { Id = 1, Type = QcType.IPQC, Result = QcResult.Pass },
            },
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parity_04_ReadyToRun_advances_to_Running_unconditionally()
    {
        var wo = new WorkOrder { CurrentStep = ProcessStepCode.ReadyToRun };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parity_05_Running_advances_to_Fqc_when_produced_qty_positive()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.Running,
            ProducedQty = 1,
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parity_06_Fqc_advances_to_Oqc_when_last_FQC_pass()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.Fqc,
            Inspections = new List<QcInspection>
            {
                new() { Id = 1, Type = QcType.FQC, Result = QcResult.Pass },
            },
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parity_07_Oqc_advances_to_Closed_when_OQC_pass_and_RohsOk()
    {
        var wo = new WorkOrder
        {
            CurrentStep = ProcessStepCode.Oqc,
            RohsOk = true,
            Inspections = new List<QcInspection>
            {
                new() { Id = 1, Type = QcType.OQC, Result = QcResult.Pass },
            },
        };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.True(r.Allowed);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Parity_08_Closed_terminal_state_cannot_advance()
    {
        var wo = new WorkOrder { CurrentStep = ProcessStepCode.Closed };
        var r = WorkOrderStateMachine.CanAdvance(wo);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.AlreadyAtFinalStep, r.Error);
    }
}
