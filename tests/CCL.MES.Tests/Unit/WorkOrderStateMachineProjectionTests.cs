using CCL.MES.Domain;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7a-1 — projection helpers <see cref="WorkOrderStateMachine.ProjectToLegacy"/>
/// + <see cref="WorkOrderStateMachine.ProjectFromLegacy"/> per contract
/// §2.1 table 2. Backfill SQL in migration
/// <c>AddWorkOrderRowVersionAndMesPhase</c> uses the same mapping; if
/// these tests fail, the migration backfill is also wrong.
/// </summary>
public sealed class WorkOrderStateMachineProjectionTests
{
    // ── MesPhase → ProcessStepCode (display projection) ─────────────

    [Theory]
    [InlineData(MesPhase.NEW,            ProcessStepCode.PrePressCheck)]
    [InlineData(MesPhase.PREPRESS,       ProcessStepCode.PrePressCheck)]
    [InlineData(MesPhase.SETTING,        ProcessStepCode.OpSetting)]
    [InlineData(MesPhase.IPQC_WAIT,      ProcessStepCode.IpqcApproval)]
    [InlineData(MesPhase.QA_PENDING,     ProcessStepCode.IpqcApproval)]
    [InlineData(MesPhase.IPQC_APPROVED,  ProcessStepCode.ReadyToRun)]
    [InlineData(MesPhase.RUNNING,        ProcessStepCode.Running)]
    [InlineData(MesPhase.PAUSED,         ProcessStepCode.Running)]
    [InlineData(MesPhase.FQC_PENDING,    ProcessStepCode.Fqc)]
    [InlineData(MesPhase.OQC_PENDING,    ProcessStepCode.Oqc)]
    [InlineData(MesPhase.DONE,           ProcessStepCode.Closed)]
    [InlineData(MesPhase.CANCELLED,      ProcessStepCode.Closed)]
    public void ProjectToLegacy_matches_contract_table(MesPhase phase, ProcessStepCode expected)
    {
        Assert.Equal(expected, WorkOrderStateMachine.ProjectToLegacy(phase));
    }

    // ── ProcessStepCode → MesPhase (write-path projection) ──────────

    [Theory]
    [InlineData(ProcessStepCode.PrePressCheck, MesPhase.PREPRESS)]
    [InlineData(ProcessStepCode.OpSetting,     MesPhase.SETTING)]
    [InlineData(ProcessStepCode.IpqcApproval,  MesPhase.IPQC_WAIT)]
    [InlineData(ProcessStepCode.ReadyToRun,    MesPhase.IPQC_APPROVED)]
    [InlineData(ProcessStepCode.Running,       MesPhase.RUNNING)]
    [InlineData(ProcessStepCode.Fqc,           MesPhase.FQC_PENDING)]
    [InlineData(ProcessStepCode.Oqc,           MesPhase.OQC_PENDING)]
    [InlineData(ProcessStepCode.Closed,        MesPhase.DONE)]
    public void ProjectFromLegacy_picks_active_member_of_collapsed_pairs(
        ProcessStepCode legacy, MesPhase expected)
    {
        Assert.Equal(expected, WorkOrderStateMachine.ProjectFromLegacy(legacy));
    }

    // ── Round-trip stability for non-collapsed phases ───────────────

    [Theory]
    [InlineData(MesPhase.SETTING)]
    [InlineData(MesPhase.IPQC_APPROVED)]
    [InlineData(MesPhase.FQC_PENDING)]
    [InlineData(MesPhase.OQC_PENDING)]
    public void Roundtrip_stable_for_phases_with_unique_legacy_slot(MesPhase phase)
    {
        var legacy = WorkOrderStateMachine.ProjectToLegacy(phase);
        var back   = WorkOrderStateMachine.ProjectFromLegacy(legacy);
        Assert.Equal(phase, back);
    }

    // ── Collapsed-pair behaviour ────────────────────────────────────

    [Theory]
    [InlineData(MesPhase.NEW,        MesPhase.PREPRESS)]   // NEW → PrePressCheck → PREPRESS (legacy active)
    [InlineData(MesPhase.PREPRESS,   MesPhase.PREPRESS)]   // stable
    [InlineData(MesPhase.QA_PENDING, MesPhase.IPQC_WAIT)]  // QA_PENDING → IpqcApproval → IPQC_WAIT
    [InlineData(MesPhase.PAUSED,     MesPhase.RUNNING)]    // PAUSED → Running → RUNNING
    [InlineData(MesPhase.CANCELLED,  MesPhase.DONE)]       // CANCELLED → Closed → DONE
    public void Roundtrip_collapses_inactive_members_onto_active(
        MesPhase original, MesPhase expectedAfterRoundtrip)
    {
        var legacy = WorkOrderStateMachine.ProjectToLegacy(original);
        var back   = WorkOrderStateMachine.ProjectFromLegacy(legacy);
        Assert.Equal(expectedAfterRoundtrip, back);
    }
}
