using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

public record TransitionResult(bool Allowed, WoErrorCode? Error = null);

/// <summary>
/// State machine governing the 7-step Work Order flow. Each transition
/// fires only when its guard is satisfied. Phase 5 — guards return a
/// language-free <see cref="WoErrorCode"/>; the Web layer maps each code
/// to a resource key via <c>WoErrorKeys</c> so the dynamic portion of the
/// UI message localises with the surrounding text. See
/// docs/PHASE5-STEP3-PLAN.md.
/// </summary>
public static class WorkOrderStateMachine
{
    public static readonly ProcessStepCode[] Flow =
    {
        ProcessStepCode.PrePressCheck,
        ProcessStepCode.OpSetting,
        ProcessStepCode.IpqcApproval,
        ProcessStepCode.ReadyToRun,
        ProcessStepCode.Running,
        ProcessStepCode.Fqc,
        ProcessStepCode.Oqc,
        ProcessStepCode.Closed
    };

    public static ProcessStepCode? Next(ProcessStepCode current)
    {
        var idx = Array.IndexOf(Flow, current);
        if (idx < 0 || idx >= Flow.Length - 1) return null;
        return Flow[idx + 1];
    }

    public static TransitionResult CanAdvance(WorkOrder wo)
    {
        var next = Next(wo.CurrentStep);
        if (next is null) return new TransitionResult(false, WoErrorCode.AlreadyAtFinalStep);

        return (wo.CurrentStep, next.Value) switch
        {
            (ProcessStepCode.PrePressCheck, ProcessStepCode.OpSetting) =>
                wo.ProductRevisionId is not null && wo.MaterialsReady
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.RequiresSpecAndMaterials),

            (ProcessStepCode.OpSetting, ProcessStepCode.IpqcApproval) =>
                wo.SetupConfirmed
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.RequiresSetupConfirmed),

            (ProcessStepCode.IpqcApproval, ProcessStepCode.ReadyToRun) =>
                wo.LastQc(QcType.IPQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.IpqcNotPassed),

            (ProcessStepCode.ReadyToRun, ProcessStepCode.Running) =>
                new TransitionResult(true),

            (ProcessStepCode.Running, ProcessStepCode.Fqc) =>
                wo.ProducedQty > 0
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.NoProductionYet),

            (ProcessStepCode.Fqc, ProcessStepCode.Oqc) =>
                wo.LastQc(QcType.FQC)?.Result == QcResult.Pass
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.FqcNotPassed),

            (ProcessStepCode.Oqc, ProcessStepCode.Closed) =>
                wo.LastQc(QcType.OQC)?.Result == QcResult.Pass && wo.RohsOk
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.OqcOrRohsNotMet),

            _ => new TransitionResult(false, WoErrorCode.InvalidStepTransition)
        };
    }

    // ───────────────────────────────────────────────────────────────
    // P10.7a-1 — canonical 12-state extensions (ADDITIVE — see
    // breakdown §1 "Port vs mirror decision" + Henry-approved
    // one-time exception). Every legacy member above this divider
    // stays unchanged; the parity sweep in
    // WorkOrderStateMachineLegacyParityTests.cs locks the contract.
    // ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Canonical 12-state order. Sequence reflects the typical happy-path
    /// transitions but is NOT a simple linear flow — branching paths
    /// (IPQC_WAIT → QA_PENDING, RUNNING ↔ PAUSED, FQC reject → PREPRESS,
    /// etc.) live in <see cref="CanTransition"/>. The array exists so
    /// callers can render an ordered timeline / status pipeline UI.
    /// </summary>
    public static readonly MesPhase[] CanonicalFlow =
    {
        MesPhase.NEW,
        MesPhase.PREPRESS,
        MesPhase.SETTING,
        MesPhase.IPQC_WAIT,
        MesPhase.QA_PENDING,
        MesPhase.IPQC_APPROVED,
        MesPhase.RUNNING,
        MesPhase.PAUSED,
        MesPhase.FQC_PENDING,
        MesPhase.OQC_PENDING,
        MesPhase.DONE,
        MesPhase.CANCELLED,
    };

    /// <summary>
    /// Deterministic projection MesPhase → legacy ProcessStepCode per
    /// contract §2.1 table 2. One-way: collapsing pairs (NEW/PREPRESS,
    /// IPQC_WAIT/QA_PENDING, RUNNING/PAUSED, DONE/CANCELLED) all map to
    /// the legacy slot occupied by their active member, so legacy
    /// readers that observe <c>CurrentStep = PrePressCheck</c> can not
    /// distinguish NEW from PREPRESS — but the new <c>MesPhase</c>
    /// column is the source of truth.
    /// </summary>
    /// <summary>
    /// Reverse projection legacy ProcessStepCode → canonical MesPhase
    /// for the BACKFILL + legacy-Advance write path. Mirrors the
    /// migration's SQL CASE backfill: collapsed pairs land on their
    /// "active" member (PrePressCheck → PREPRESS, Running → RUNNING)
    /// because legacy never observed the inactive members
    /// (NEW is system-only, PAUSED was a side-channel bool, etc.).
    /// </summary>
    public static MesPhase ProjectFromLegacy(ProcessStepCode step) => step switch
    {
        ProcessStepCode.PrePressCheck => MesPhase.PREPRESS,
        ProcessStepCode.OpSetting     => MesPhase.SETTING,
        ProcessStepCode.IpqcApproval  => MesPhase.IPQC_WAIT,
        ProcessStepCode.ReadyToRun    => MesPhase.IPQC_APPROVED,
        ProcessStepCode.Running       => MesPhase.RUNNING,
        ProcessStepCode.Fqc           => MesPhase.FQC_PENDING,
        ProcessStepCode.Oqc           => MesPhase.OQC_PENDING,
        ProcessStepCode.Closed        => MesPhase.DONE,
        _                             => MesPhase.NEW,
    };

    public static ProcessStepCode ProjectToLegacy(MesPhase phase) => phase switch
    {
        MesPhase.NEW            => ProcessStepCode.PrePressCheck,
        MesPhase.PREPRESS       => ProcessStepCode.PrePressCheck,
        MesPhase.SETTING        => ProcessStepCode.OpSetting,
        MesPhase.IPQC_WAIT      => ProcessStepCode.IpqcApproval,
        MesPhase.QA_PENDING     => ProcessStepCode.IpqcApproval,
        MesPhase.IPQC_APPROVED  => ProcessStepCode.ReadyToRun,
        MesPhase.RUNNING        => ProcessStepCode.Running,
        MesPhase.PAUSED         => ProcessStepCode.Running,
        MesPhase.FQC_PENDING    => ProcessStepCode.Fqc,
        MesPhase.OQC_PENDING    => ProcessStepCode.Oqc,
        MesPhase.DONE           => ProcessStepCode.Closed,
        MesPhase.CANCELLED      => ProcessStepCode.Closed,
        _                       => ProcessStepCode.PrePressCheck,
    };

    /// <summary>
    /// Transition matrix per contract §3.1. Returns true if the
    /// <paramref name="from"/> → <paramref name="to"/> edge is allowed
    /// or conditionally-allowed at the matrix level (the full
    /// condition / signoff / recovery enforcement lands in PR 7a-1.3
    /// when the live endpoint surface ships; this method is the static
    /// "is the edge in the grid at all" check).
    ///
    /// Self-loops (from == to) return false. Terminal sources (DONE,
    /// CANCELLED) return false regardless of target — contract §2.2
    /// "Terminal states must never become entry source". Any target
    /// that is recovery-only (e.g. <c>* → CANCELLED</c>) is reported
    /// as <see cref="MesTransitionKind.RecoveryOnly"/> via
    /// <see cref="ClassifyTransition"/>.
    /// </summary>
    public static bool IsMatrixAllowed(MesPhase from, MesPhase to)
        => ClassifyTransition(from, to) is
            MesTransitionKind.Allowed
            or MesTransitionKind.RequiresCondition
            or MesTransitionKind.RequiresSignoff
            or MesTransitionKind.RecoveryOnly;

    /// <summary>
    /// Classify a (from, to) edge per contract §3.1 + §3.2 cells.
    /// Used by xUnit transition matrix tests + by the runtime guard
    /// in <see cref="CanTransition"/>.
    /// </summary>
    public static MesTransitionKind ClassifyTransition(MesPhase from, MesPhase to)
    {
        if (from == to) return MesTransitionKind.Blocked;

        // Terminal sources cannot exit (contract §2.2). Reworks open a
        // fresh WO; no "uncancel" or "reopen DONE" event exists.
        if (from is MesPhase.DONE or MesPhase.CANCELLED) return MesTransitionKind.Blocked;

        // Any non-terminal source → CANCELLED is recovery-only (admin/sys).
        if (to == MesPhase.CANCELLED) return MesTransitionKind.RecoveryOnly;

        return (from, to) switch
        {
            // NEW (system-only entry)
            (MesPhase.NEW, MesPhase.PREPRESS) => MesTransitionKind.Allowed,

            // PREPRESS
            (MesPhase.PREPRESS, MesPhase.SETTING) => MesTransitionKind.RequiresCondition,

            // SETTING
            (MesPhase.SETTING, MesPhase.IPQC_WAIT) => MesTransitionKind.Allowed,
            (MesPhase.SETTING, MesPhase.PREPRESS) => MesTransitionKind.RecoveryOnly,

            // IPQC_WAIT
            (MesPhase.IPQC_WAIT, MesPhase.IPQC_APPROVED) => MesTransitionKind.RequiresSignoff,
            (MesPhase.IPQC_WAIT, MesPhase.QA_PENDING)    => MesTransitionKind.RequiresSignoff,
            (MesPhase.IPQC_WAIT, MesPhase.PREPRESS)      => MesTransitionKind.RequiresSignoff,

            // QA_PENDING
            (MesPhase.QA_PENDING, MesPhase.IPQC_APPROVED) => MesTransitionKind.RequiresSignoff,
            (MesPhase.QA_PENDING, MesPhase.PREPRESS)      => MesTransitionKind.RequiresSignoff,

            // IPQC_APPROVED
            (MesPhase.IPQC_APPROVED, MesPhase.RUNNING) => MesTransitionKind.Allowed,

            // RUNNING
            (MesPhase.RUNNING, MesPhase.PAUSED)      => MesTransitionKind.RequiresCondition,
            (MesPhase.RUNNING, MesPhase.FQC_PENDING) => MesTransitionKind.RequiresCondition,

            // PAUSED
            (MesPhase.PAUSED, MesPhase.RUNNING) => MesTransitionKind.Allowed,

            // FQC_PENDING
            (MesPhase.FQC_PENDING, MesPhase.OQC_PENDING) => MesTransitionKind.RequiresSignoff,
            (MesPhase.FQC_PENDING, MesPhase.PREPRESS)    => MesTransitionKind.RequiresSignoff,

            // OQC_PENDING (signed §10.2 answer: OQC_REJECT → FQC_PENDING, not full rework)
            (MesPhase.OQC_PENDING, MesPhase.DONE)        => MesTransitionKind.RequiresSignoff,
            (MesPhase.OQC_PENDING, MesPhase.FQC_PENDING) => MesTransitionKind.RequiresSignoff,

            _ => MesTransitionKind.Blocked,
        };
    }

    /// <summary>
    /// P10.7a-2.2 — convenience predicate for the admin force-phase
    /// endpoint. True iff the (<paramref name="from"/>, <paramref name="to"/>)
    /// edge is classified as <see cref="MesTransitionKind.RecoveryOnly"/>
    /// per contract §3.1. Exactly 11 cells of the 144-cell grid qualify:
    /// SETTING → PREPRESS (the §8.1 archetype) + 10× *→CANCELLED
    /// (every non-terminal source). Returns false for:
    ///   * self-loops (from == to)
    ///   * terminal sources (DONE, CANCELLED) — §2.2 terminal-no-revive
    ///   * any target other than CANCELLED for non-SETTING sources
    ///   * any *→DONE / *→NEW edge
    ///   * blocked / allowed / requires-condition / requires-signoff cells
    /// The admin /force-phase endpoint MUST call this before mutation
    /// so 133 of the 144 cells decline with 409 unforceable_transition.
    /// </summary>
    public static bool IsForceablePhase(MesPhase from, MesPhase to)
        => ClassifyTransition(from, to) == MesTransitionKind.RecoveryOnly;

    /// <summary>
    /// Domain-level "may this transition fire?" check. Returns
    /// <c>false</c> + a reason if (a) the matrix forbids the edge,
    /// (b) a <see cref="MesTransitionKind.RequiresCondition"/> cell's
    /// predicate is unmet, OR (c) the edge is
    /// <see cref="MesTransitionKind.RecoveryOnly"/> / RequiresSignoff
    /// and the caller has not flagged the appropriate signoff.
    ///
    /// PR 7a-1.1 ships the matrix + condition logic; PR 7a-1.3 wires
    /// the signoff actor + admin-vs-self check + idempotency through
    /// the HTTP layer. Until PR 7a-1.3 lands, a caller that asks for
    /// a signoff-required transition with
    /// <paramref name="signoffPresent"/> = false gets back a clear
    /// <see cref="WoErrorCode.InvalidStepTransition"/> (caller-side
    /// surface remains opt-in).
    /// </summary>
    public static TransitionResult CanTransition(
        WorkOrder wo,
        MesPhase from,
        MesPhase to,
        bool signoffPresent = false,
        bool adminOverride = false)
    {
        var kind = ClassifyTransition(from, to);
        return kind switch
        {
            MesTransitionKind.Blocked => new TransitionResult(false, WoErrorCode.InvalidStepTransition),

            MesTransitionKind.Allowed => new TransitionResult(true),

            MesTransitionKind.RequiresCondition =>
                CheckCondition(wo, from, to),

            MesTransitionKind.RequiresSignoff =>
                signoffPresent
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.InvalidStepTransition),

            MesTransitionKind.RecoveryOnly =>
                adminOverride
                    ? new TransitionResult(true)
                    : new TransitionResult(false, WoErrorCode.InvalidStepTransition),

            _ => new TransitionResult(false, WoErrorCode.InvalidStepTransition),
        };
    }

    private static TransitionResult CheckCondition(WorkOrder wo, MesPhase from, MesPhase to)
    {
        // PREPRESS → SETTING: contract §3.2 — all child-row checks OK.
        // Until the wo_materials / wo_plate_check / wo_cutter_check
        // tables land (P10.7a-2 BOM-snapshot loader), the closest legacy
        // analog is the legacy MaterialsReady + ProductRevisionId pair.
        // Mirror the legacy CanAdvance(PrePressCheck → OpSetting) guard
        // here so the parity contract holds for the projection.
        if (from == MesPhase.PREPRESS && to == MesPhase.SETTING)
        {
            return wo.ProductRevisionId is not null && wo.MaterialsReady
                ? new TransitionResult(true)
                : new TransitionResult(false, WoErrorCode.RequiresSpecAndMaterials);
        }

        // RUNNING → FQC_PENDING: contract §3.2 — ProducedQty > 0.
        // Identical guard to legacy CanAdvance(Running → Fqc); the
        // canonical edge re-uses the same predicate so the projection
        // is consistent (legacy callers observe the same allow/deny).
        if (from == MesPhase.RUNNING && to == MesPhase.FQC_PENDING)
        {
            return wo.ProducedQty > 0
                ? new TransitionResult(true)
                : new TransitionResult(false, WoErrorCode.NoProductionYet);
        }

        // RUNNING → PAUSED: contract §3.2 requires non-empty
        // downtime_reasons.code. That table lands in P10.7a-2; until
        // then the matrix allows the edge unconditionally and the
        // caller-side guard is enforced at the HTTP layer (PR 7a-1.3).
        if (from == MesPhase.RUNNING && to == MesPhase.PAUSED)
        {
            return new TransitionResult(true);
        }

        // Defensive default — every RequiresCondition cell above should
        // be enumerated. If a new cell lands in the matrix without a
        // matching predicate here, fail closed (caller sees
        // InvalidStepTransition + an xUnit row in the
        // ConditionVariantTests fails immediately).
        return new TransitionResult(false, WoErrorCode.InvalidStepTransition);
    }
}

/// <summary>
/// P10.7a-1 — classification of a (from, to) cell in the canonical
/// transition matrix per contract §3.1. Used by the xUnit matrix
/// sweep + by <see cref="WorkOrderStateMachine.CanTransition"/>.
/// </summary>
public enum MesTransitionKind
{
    /// <summary>No event can produce this transition.</summary>
    Blocked,

    /// <summary>Transition fires on its triggering event; no extra
    /// domain predicate.</summary>
    Allowed,

    /// <summary>A domain predicate must hold (e.g. ProducedQty &gt; 0
    /// for RUNNING → FQC_PENDING).</summary>
    RequiresCondition,

    /// <summary>A separate actor must present 4-eye / step-up
    /// credentials (IPQC / FQC / OQC / QA Manager).</summary>
    RequiresSignoff,

    /// <summary>Admin / sys-only path that emits a
    /// <c>SYS_RECOVERY</c> audit row.</summary>
    RecoveryOnly,
}
