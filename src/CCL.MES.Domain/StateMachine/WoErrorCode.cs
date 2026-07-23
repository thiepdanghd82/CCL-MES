namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// Phase 5 — Work Order error codes emitted by the state machine and the
/// service layer. Domain stays language-free: this enum carries WHAT
/// happened; the Web layer maps each value to a localized resource key via
/// <c>CCL.MES.Web.Services.WoErrorKeys</c>. See docs/PHASE5-STEP3-PLAN.md.
/// </summary>
public enum WoErrorCode
{
    /// <summary>Already at the final step in the 7-step flow.</summary>
    AlreadyAtFinalStep,

    /// <summary>Pre-press → OP-setting needs approved Spec + ready materials.</summary>
    RequiresSpecAndMaterials,

    /// <summary>OP-setting → IPQC needs setup confirmation.</summary>
    RequiresSetupConfirmed,

    /// <summary>IPQC → Ready-to-run needs a passing IPQC inspection.</summary>
    IpqcNotPassed,

    /// <summary>Running → FQC needs ProducedQty &gt; 0.</summary>
    NoProductionYet,

    /// <summary>FQC → OQC needs a passing FQC inspection.</summary>
    FqcNotPassed,

    /// <summary>OQC → Closed needs a passing OQC inspection + RoHS ok.</summary>
    OqcOrRohsNotMet,

    /// <summary>Catch-all for an unknown (current, next) pair.</summary>
    InvalidStepTransition,

    /// <summary>Service layer: the requested WO id does not exist.</summary>
    WorkOrderNotFound,

    // ── P11-1 — Multi-Method Routing DAG (fork-join) ────────────────

    /// <summary>SPLIT → FQC_PENDING: chưa phải mọi leg terminal đạt
    /// LegPhase.LEG_DONE (join gate chưa thỏa).</summary>
    LegsNotAllDone,

    /// <summary>PREPRESS → SPLIT: routing DAG không hợp lệ (cycle, leg
    /// mồ côi, hoặc &lt;2 leg). Xem <c>RoutingDagValidator</c>.</summary>
    InvalidRoutingDag,

    /// <summary>ASSEMBLY leg thiếu input tiên quyết (không đủ dep PRINT +
    /// TAPE khi InputSource=IN_LINE).</summary>
    AssemblyInputsMissing,

    /// <summary>Routing op không map được sang leg (RoutingLegMap thiếu
    /// luật) — caller log loud + hỏi người duyệt, KHÔNG đoán.</summary>
    RoutingUnmapped,
}
