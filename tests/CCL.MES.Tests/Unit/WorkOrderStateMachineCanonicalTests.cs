using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7a-1 — canonical 12-state transition matrix subset for the
/// domain foundation PR. Full 144-cell matrix lands in PR 7a-1.4
/// (test belt). This file covers the happy-path edges + the
/// terminal-source rule + the recovery-only classification so the
/// foundation is meaningfully tested without committing to the full
/// matrix in one go.
/// </summary>
public sealed class WorkOrderStateMachineCanonicalTests
{
    // ── ClassifyTransition — happy-path edges ─────────────────────────

    [Theory]
    [InlineData(MesPhase.NEW,            MesPhase.PREPRESS,       MesTransitionKind.Allowed)]
    [InlineData(MesPhase.SETTING,        MesPhase.IPQC_WAIT,      MesTransitionKind.Allowed)]
    [InlineData(MesPhase.IPQC_APPROVED,  MesPhase.RUNNING,        MesTransitionKind.Allowed)]
    [InlineData(MesPhase.PAUSED,         MesPhase.RUNNING,        MesTransitionKind.Allowed)]
    public void Classify_Allowed_cells_per_contract(
        MesPhase from, MesPhase to, MesTransitionKind expected)
    {
        Assert.Equal(expected, WorkOrderStateMachine.ClassifyTransition(from, to));
    }

    [Theory]
    [InlineData(MesPhase.PREPRESS, MesPhase.SETTING)]   // child-row checks OK
    [InlineData(MesPhase.RUNNING,  MesPhase.FQC_PENDING)] // ProducedQty > 0
    [InlineData(MesPhase.RUNNING,  MesPhase.PAUSED)]   // downtime_reasons.code
    public void Classify_RequiresCondition_cells(MesPhase from, MesPhase to)
    {
        Assert.Equal(
            MesTransitionKind.RequiresCondition,
            WorkOrderStateMachine.ClassifyTransition(from, to));
    }

    [Theory]
    [InlineData(MesPhase.IPQC_WAIT,    MesPhase.IPQC_APPROVED)] // IPQC step-up
    [InlineData(MesPhase.IPQC_WAIT,    MesPhase.QA_PENDING)]    // IPQC special accept
    [InlineData(MesPhase.IPQC_WAIT,    MesPhase.PREPRESS)]      // IPQC stop line
    [InlineData(MesPhase.QA_PENDING,   MesPhase.IPQC_APPROVED)] // QA approve special
    [InlineData(MesPhase.QA_PENDING,   MesPhase.PREPRESS)]      // QA reject special
    [InlineData(MesPhase.FQC_PENDING,  MesPhase.OQC_PENDING)]   // FQC pass
    [InlineData(MesPhase.FQC_PENDING,  MesPhase.PREPRESS)]      // FQC reject
    [InlineData(MesPhase.OQC_PENDING,  MesPhase.DONE)]          // OQC pass
    [InlineData(MesPhase.OQC_PENDING,  MesPhase.FQC_PENDING)]   // OQC reject → FQC (signed §10.2)
    public void Classify_RequiresSignoff_cells(MesPhase from, MesPhase to)
    {
        Assert.Equal(
            MesTransitionKind.RequiresSignoff,
            WorkOrderStateMachine.ClassifyTransition(from, to));
    }

    [Theory]
    [InlineData(MesPhase.NEW,            MesPhase.CANCELLED)]
    [InlineData(MesPhase.PREPRESS,       MesPhase.CANCELLED)]
    [InlineData(MesPhase.SETTING,        MesPhase.CANCELLED)]
    [InlineData(MesPhase.IPQC_WAIT,      MesPhase.CANCELLED)]
    [InlineData(MesPhase.QA_PENDING,     MesPhase.CANCELLED)]
    [InlineData(MesPhase.IPQC_APPROVED,  MesPhase.CANCELLED)]
    [InlineData(MesPhase.RUNNING,        MesPhase.CANCELLED)]
    [InlineData(MesPhase.PAUSED,         MesPhase.CANCELLED)]
    [InlineData(MesPhase.FQC_PENDING,    MesPhase.CANCELLED)]
    [InlineData(MesPhase.OQC_PENDING,    MesPhase.CANCELLED)]
    [InlineData(MesPhase.SETTING,        MesPhase.PREPRESS)] // setting abort
    public void Classify_RecoveryOnly_cells(MesPhase from, MesPhase to)
    {
        Assert.Equal(
            MesTransitionKind.RecoveryOnly,
            WorkOrderStateMachine.ClassifyTransition(from, to));
    }

    // ── Terminal-source rule ─────────────────────────────────────────

    [Theory]
    [InlineData(MesPhase.DONE,      MesPhase.NEW)]
    [InlineData(MesPhase.DONE,      MesPhase.PREPRESS)]
    [InlineData(MesPhase.DONE,      MesPhase.RUNNING)]
    [InlineData(MesPhase.DONE,      MesPhase.CANCELLED)]
    [InlineData(MesPhase.CANCELLED, MesPhase.NEW)]
    [InlineData(MesPhase.CANCELLED, MesPhase.PREPRESS)]
    [InlineData(MesPhase.CANCELLED, MesPhase.DONE)]
    public void Terminal_sources_block_every_target(MesPhase from, MesPhase to)
    {
        Assert.Equal(
            MesTransitionKind.Blocked,
            WorkOrderStateMachine.ClassifyTransition(from, to));
    }

    // ── Self-loops ───────────────────────────────────────────────────

    [Theory]
    [InlineData(MesPhase.NEW)]
    [InlineData(MesPhase.RUNNING)]
    [InlineData(MesPhase.DONE)]
    [InlineData(MesPhase.CANCELLED)]
    public void Self_loop_always_blocked(MesPhase phase)
    {
        Assert.Equal(
            MesTransitionKind.Blocked,
            WorkOrderStateMachine.ClassifyTransition(phase, phase));
    }

    // ── CanTransition — condition predicate enforcement ──────────────

    [Fact]
    public void Running_to_Fqc_requires_producedQty_positive()
    {
        var wo = new WorkOrder { ProducedQty = 0 };
        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.RUNNING, MesPhase.FQC_PENDING);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.NoProductionYet, r.Error);
    }

    [Fact]
    public void Running_to_Fqc_passes_when_produced_qty_positive()
    {
        var wo = new WorkOrder { ProducedQty = 1 };
        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.RUNNING, MesPhase.FQC_PENDING);
        Assert.True(r.Allowed);
    }

    [Fact]
    public void Prepress_to_Setting_requires_spec_and_materials()
    {
        var wo = new WorkOrder { ProductRevisionId = null, MaterialsReady = false };
        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.PREPRESS, MesPhase.SETTING);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.RequiresSpecAndMaterials, r.Error);
    }

    [Fact]
    public void Prepress_to_Setting_passes_when_spec_and_materials_present()
    {
        var wo = new WorkOrder { ProductRevisionId = 1, MaterialsReady = true };
        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.PREPRESS, MesPhase.SETTING);
        Assert.True(r.Allowed);
    }

    // ── CanTransition — signoff gating ───────────────────────────────

    [Fact]
    public void Signoff_required_cells_block_without_signoff_flag()
    {
        var wo = new WorkOrder();
        var r = WorkOrderStateMachine.CanTransition(
            wo, MesPhase.IPQC_WAIT, MesPhase.IPQC_APPROVED, signoffPresent: false);
        Assert.False(r.Allowed);
    }

    [Fact]
    public void Signoff_required_cells_pass_with_signoff_flag()
    {
        var wo = new WorkOrder();
        var r = WorkOrderStateMachine.CanTransition(
            wo, MesPhase.IPQC_WAIT, MesPhase.IPQC_APPROVED, signoffPresent: true);
        Assert.True(r.Allowed);
    }

    // ── CanTransition — recovery gating ──────────────────────────────

    [Fact]
    public void Recovery_only_cells_block_without_admin_override()
    {
        var wo = new WorkOrder();
        var r = WorkOrderStateMachine.CanTransition(
            wo, MesPhase.RUNNING, MesPhase.CANCELLED, adminOverride: false);
        Assert.False(r.Allowed);
    }

    [Fact]
    public void Recovery_only_cells_pass_with_admin_override()
    {
        var wo = new WorkOrder();
        var r = WorkOrderStateMachine.CanTransition(
            wo, MesPhase.RUNNING, MesPhase.CANCELLED, adminOverride: true);
        Assert.True(r.Allowed);
    }

    // ── CanonicalFlow ────────────────────────────────────────────────

    [Fact]
    public void CanonicalFlow_lists_all_12_phases_in_canonical_order()
    {
        Assert.Equal(12, WorkOrderStateMachine.CanonicalFlow.Length);
        Assert.Equal(MesPhase.NEW,       WorkOrderStateMachine.CanonicalFlow[0]);
        Assert.Equal(MesPhase.CANCELLED, WorkOrderStateMachine.CanonicalFlow[11]);
    }
}
