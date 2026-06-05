namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// P10.7a-1 — canonical 12-state work-order phase model per
/// <c>docs/P10.7-WO-STATE-CONTRACT.md</c> §2.1. Strict superset of the
/// legacy 8-state <see cref="ProcessStepCode"/> enum + matches the
/// SpecHub <c>mes_status</c> 10-state set + 2 late-flow QC gates
/// (<see cref="FQC_PENDING"/>, <see cref="OQC_PENDING"/>) that legacy
/// already ships as <c>ProcessStepCode.Fqc</c> / <c>Oqc</c>.
///
/// Stored in the database as a string (HasMaxLength(16) + HasConversion).
/// The MesPhase → <see cref="ProcessStepCode"/> projection is one-way
/// + deterministic, implemented in
/// <see cref="WorkOrderStateMachine.ProjectToLegacy"/>; legacy Razor
/// pages keep reading <c>WorkOrder.CurrentStep</c> unchanged.
/// </summary>
public enum MesPhase
{
    /// <summary>WO record exists; no scanner has touched it yet.</summary>
    NEW = 0,

    /// <summary>Operator scanned WO; row-level Materials/Plate/Cutter
    /// checks being filled.</summary>
    PREPRESS = 1,

    /// <summary>Press setup in progress; setting timer running.</summary>
    SETTING = 2,

    /// <summary>Setup done; IPQC inspector not yet signed in.</summary>
    IPQC_WAIT = 3,

    /// <summary>IPQC SPECIAL_ACCEPT judgement made; QA Manager
    /// approval pending.</summary>
    QA_PENDING = 4,

    /// <summary>IPQC GO_RUN (or QA-approved special accept).</summary>
    IPQC_APPROVED = 5,

    /// <summary>Production session active; run_events accruing
    /// QTY_ADD / scrap.</summary>
    RUNNING = 6,

    /// <summary>Operator paused current run_session; downtime reason
    /// captured.</summary>
    PAUSED = 7,

    /// <summary>Run finished; FQC inspector not yet signed in.</summary>
    FQC_PENDING = 8,

    /// <summary>FQC passed; OQC inspector not yet signed in.</summary>
    OQC_PENDING = 9,

    /// <summary>Terminal — OQC passed + RohsOk + 3-signature gate.</summary>
    DONE = 10,

    /// <summary>Terminal — admin/sys-only cancellation. Maps to SpecHub
    /// STOPPED. Reworks open a fresh WO instead of reviving this row.</summary>
    CANCELLED = 11,
}
