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

    /// <summary>P10.7e-1 Q1 — terminal closed.done after OQC Pass + 3-sig
    /// gate (Q5). SpecHub MES_QUALITY_REDESIGN_PLAN.md §Phase 2 lines
    /// 47-48 + 59. Distinct from <see cref="DONE"/> so a future ERP
    /// push (P10.7-BACKLOG OOS-A) can filter on "ready to invoice".
    /// Reaching SHIPPED requires OQC Approver Pass with
    /// Inspector ≠ Reviewer ≠ Approver. No transitions OUT of SHIPPED —
    /// rework opens a fresh WO referencing this one.
    ///
    /// 7e-1 also narrows <see cref="DONE"/> semantics: new operator
    /// flow uses DONE as a TRANSIENT post-RUNNING state (operator's
    /// Finish click; server auto-cascades DONE → FQC_PENDING in the
    /// same SaveChanges per the 7c-2 pattern). Legacy DONE rows from
    /// pre-7e WOs stay valid as terminal — the §3.1 grid keeps
    /// DONE → FQC_PENDING as RequiresCondition so a legacy row
    /// can't accidentally advance unless the controller explicitly
    /// drives it.</summary>
    SHIPPED = 12,
}
