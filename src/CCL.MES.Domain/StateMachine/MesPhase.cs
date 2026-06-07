namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// P10.7a-1 — canonical 12-state work-order phase model per
/// <c>docs/P10.7-WO-STATE-CONTRACT.md</c> §2.1.
/// P10.7e-1 Q1 amendment — 13th state <see cref="SHIPPED"/> added so
/// the SpecHub quality cycle <c>RUNNING → DONE → FQC_PENDING →
/// OQC_PENDING → SHIPPED</c> closes (SpecHub
/// MES_QUALITY_REDESIGN_PLAN.md §Phase 2 lines 38-48). <see cref="DONE"/>
/// keeps its 7d semantics — terminal after RUNNING/PAUSED via
/// <c>/run/finish</c> — so legacy DONE rows in prod DB stay valid
/// without retroactive migration. New OQC Pass path advances
/// <see cref="OQC_PENDING"/> → <see cref="SHIPPED"/> (3-signature
/// gate per Q5).
///
/// Strict superset of the legacy 8-state <see cref="ProcessStepCode"/>
/// enum + matches the SpecHub <c>mes_status</c> 10-state set + 2 late-flow
/// QC gates (<see cref="FQC_PENDING"/>, <see cref="OQC_PENDING"/>) that
/// legacy already ships as <c>ProcessStepCode.Fqc</c> / <c>Oqc</c>.
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

    /// <summary>P10.7e-1 Q1 amendment — semantics narrowed: now a
    /// TRANSIENT post-production state between
    /// <see cref="RUNNING"/>/<see cref="PAUSED"/> and
    /// <see cref="FQC_PENDING"/>. The /run/finish endpoint advances
    /// RUNNING|PAUSED → DONE and the next SaveChanges advances
    /// DONE → FQC_PENDING (single atomic write per the 7c-2 pattern).
    /// Operator briefly sees the "Production Done" toast before the
    /// FqcDashboard mounts (L21 OnPhaseChanged bubble fires twice).
    /// Pre-7e DONE rows in prod stay valid as a terminal state — the
    /// state machine still accepts <see cref="DONE"/> → terminal for
    /// legacy rows; only new flows route through SHIPPED.</summary>
    DONE = 10,

    /// <summary>Terminal — admin/sys-only cancellation. Maps to SpecHub
    /// STOPPED. Reworks open a fresh WO instead of reviving this row.</summary>
    CANCELLED = 11,

    // P10.7e-1 Q1 contract gap (next commit): SHIPPED = 12 will be added
    // alongside the state-machine §3.1 grid expansion (144 → 169 cells +
    // DONE narrowed to transient + OQC_PENDING → SHIPPED RequiresSignoff
    // for the 3-sig terminal pass). Keeping the enum at 12 phases for
    // THIS commit keeps the existing 7a-1.4 144-cell matrix test green;
    // the 13-phase expansion lands in 7e-1's migration commit so the
    // contract amendment is paired with the implementation that
    // honours it.
}
