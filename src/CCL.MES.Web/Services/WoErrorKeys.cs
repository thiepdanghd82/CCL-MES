using CCL.MES.Domain.StateMachine;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 5 — maps a Domain <see cref="WoErrorCode"/> to the resource key
/// of its localized message. Domain stays language-free; the Web layer
/// owns the i18n keys (workorders.error.* in SharedResource.resx +
/// SharedResource.vi.resx). See docs/PHASE5-STEP3-PLAN.md.
/// </summary>
public static class WoErrorKeys
{
    // Exhaustive map: every WoErrorCode value MUST appear here. A missing
    // entry throws at first use, surfacing the gap in dev before prod.
    private static readonly Dictionary<WoErrorCode, string> _map = new()
    {
        [WoErrorCode.AlreadyAtFinalStep]       = "workorders.error.already_at_final_step",
        [WoErrorCode.RequiresSpecAndMaterials] = "workorders.error.requires_spec_materials",
        [WoErrorCode.RequiresSetupConfirmed]   = "workorders.error.requires_setup_confirmed",
        [WoErrorCode.IpqcNotPassed]            = "workorders.error.ipqc_not_passed",
        [WoErrorCode.NoProductionYet]          = "workorders.error.no_production_yet",
        [WoErrorCode.FqcNotPassed]             = "workorders.error.fqc_not_passed",
        [WoErrorCode.OqcOrRohsNotMet]          = "workorders.error.oqc_or_rohs_not_met",
        [WoErrorCode.InvalidStepTransition]    = "workorders.error.invalid_transition",
        [WoErrorCode.WorkOrderNotFound]        = "workorders.error.wo_not_found",
    };

    public static string KeyFor(WoErrorCode? code) =>
        code is null ? "workorders.error.unknown" : _map[code.Value];
}
