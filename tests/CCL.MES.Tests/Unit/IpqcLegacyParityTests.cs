using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7d-1 — legacy parity guard (locked, Trait=LegacyParity).
///
/// Mirrors the 7b <see cref="PrepressLegacyParityTests"/> pattern.
/// 7d-1 is ADDITIVE: introduces the <c>WoIpqcCheck</c> child row +
/// IpqcReadinessRollup helper, but MUST NOT alter the existing
/// 144-cell <see cref="WorkOrderStateMachine.ClassifyTransition"/>
/// matrix for the IPQC-related cells (IPQC_WAIT → IPQC_APPROVED /
/// QA_PENDING / PREPRESS, QA_PENDING → IPQC_APPROVED / PREPRESS).
///
/// These fixtures are the alarm bell: any future PR that flips one
/// of these cells from RequiresSignoff to anything else (or worse,
/// to Allowed without ceremony) fails CI here.
/// </summary>
public sealed class IpqcLegacyParityTests
{
    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Cell_IPQC_WAIT_to_IPQC_APPROVED_stays_RequiresSignoff()
    {
        // Per SpecHub §3 line 142: judgment "Go Run" → IPQC_APPROVED.
        // The contract calls this a "signoff" — QC must explicitly
        // submit; no auto-advance allowed.
        var k = WorkOrderStateMachine.ClassifyTransition(
            MesPhase.IPQC_WAIT, MesPhase.IPQC_APPROVED);
        Assert.Equal(MesTransitionKind.RequiresSignoff, k);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Cell_IPQC_WAIT_to_QA_PENDING_stays_RequiresSignoff()
    {
        // SpecHub §3 line 144: SPECIAL_ACCEPT escalation.
        var k = WorkOrderStateMachine.ClassifyTransition(
            MesPhase.IPQC_WAIT, MesPhase.QA_PENDING);
        Assert.Equal(MesTransitionKind.RequiresSignoff, k);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Cell_IPQC_WAIT_to_PREPRESS_stays_RequiresSignoff()
    {
        // SpecHub §3 line 143: STOP_LINE judgment.
        var k = WorkOrderStateMachine.ClassifyTransition(
            MesPhase.IPQC_WAIT, MesPhase.PREPRESS);
        Assert.Equal(MesTransitionKind.RequiresSignoff, k);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Cell_QA_PENDING_to_IPQC_APPROVED_stays_RequiresSignoff()
    {
        // SpecHub §3 line 148: QA Approve.
        var k = WorkOrderStateMachine.ClassifyTransition(
            MesPhase.QA_PENDING, MesPhase.IPQC_APPROVED);
        Assert.Equal(MesTransitionKind.RequiresSignoff, k);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Cell_QA_PENDING_to_PREPRESS_stays_RequiresSignoff()
    {
        // SpecHub §3 line 149: QA Reject = reset like Stop Line.
        var k = WorkOrderStateMachine.ClassifyTransition(
            MesPhase.QA_PENDING, MesPhase.PREPRESS);
        Assert.Equal(MesTransitionKind.RequiresSignoff, k);
    }

    [Fact]
    [Trait("Category", "LegacyParity")]
    public void Cells_NOT_modeled_by_7d_stay_unchanged_for_IPQC_phase()
    {
        // Anything else from IPQC_WAIT that isn't in the 3-edge
        // judgment fan-out stays Blocked (no direct hop to RUNNING).
        Assert.Equal(MesTransitionKind.Blocked,
            WorkOrderStateMachine.ClassifyTransition(MesPhase.IPQC_WAIT, MesPhase.RUNNING));
        Assert.Equal(MesTransitionKind.Blocked,
            WorkOrderStateMachine.ClassifyTransition(MesPhase.IPQC_WAIT, MesPhase.PAUSED));
        Assert.Equal(MesTransitionKind.Blocked,
            WorkOrderStateMachine.ClassifyTransition(MesPhase.IPQC_WAIT, MesPhase.FQC_PENDING));
    }
}
