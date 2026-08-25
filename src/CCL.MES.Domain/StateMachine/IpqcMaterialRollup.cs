using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// IPQC first-article — pure helper computing the MATERIAL (SYSTEM) readiness
/// gate over the per-BOM-line <see cref="WoIpqcMaterialCheck"/> rows. Mirrors
/// <see cref="IpqcReadinessRollup"/> so the controller can AND this into the
/// existing item/slot readiness before enabling judgment.
///
/// "Resolved" (Henry Q1 — soft-lock) is driven by the waiver state stamped at
/// confirm-time, NOT by the operator's OK/NG alone:
///   - NotRequired (lot matched IQC at confirm): resolved iff Status == Ok.
///     A genuine physical NG on a matched lot is NOT resolved (must StopLine).
///   - Approved (Engineer waived a divergence): resolved regardless of OK/NG.
///   - PendingEngineer / Rejected: NOT resolved. This is the soft lock — even
///     an operator OK on a DIVERGENT lot cannot self-resolve; an Engineer must
///     sign the waiver first.
///
/// Empty set (WO carries no BOM material rows) → AllResolved = true: absence of
/// materials never blocks judgment (legacy parity with pre-first-article WOs).
/// </summary>
public static class IpqcMaterialRollup
{
    /// <summary>A single row counts as resolved for the GoRun gate.</summary>
    public static bool IsResolved(WoIpqcMaterialCheck m) => m.DivergenceApprovalStatus switch
    {
        DivergenceApprovalStatus.NotRequired => m.Status == IpqcCheckStatus.Ok,
        DivergenceApprovalStatus.Approved    => true,
        _                                    => false, // PendingEngineer / Rejected
    };

    public static (bool AllResolved, bool AnyPendingWaiver, bool AnyRejected) Compute(
        IReadOnlyCollection<WoIpqcMaterialCheck>? rows)
    {
        if (rows is not { Count: > 0 })
            return (true, false, false);

        return (
            rows.All(IsResolved),
            rows.Any(r => r.DivergenceApprovalStatus == DivergenceApprovalStatus.PendingEngineer),
            rows.Any(r => r.DivergenceApprovalStatus == DivergenceApprovalStatus.Rejected));
    }
}
