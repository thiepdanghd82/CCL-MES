using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// IPQC first-article — pure helper computing the MATERIAL (SYSTEM) readiness
/// gate over the per-BOM-line <see cref="WoIpqcMaterialCheck"/> rows. Mirrors
/// <see cref="IpqcReadinessRollup"/> so the controller can AND this into the
/// existing item/slot readiness before enabling judgment.
///
/// "Resolved" (Henry Q1 — soft-lock) = the row is OK, OR it is NG-due-to-
/// divergence but an Engineer has APPROVED the waiver. A plain NG with no
/// approved waiver is NOT resolved (a genuine material NG must StopLine).
///
/// Empty set (WO carries no BOM material rows) → AllResolved = true: absence of
/// materials never blocks judgment (legacy parity with pre-first-article WOs).
/// </summary>
public static class IpqcMaterialRollup
{
    /// <summary>A single row counts as resolved for the judgment gate.</summary>
    public static bool IsResolved(WoIpqcMaterialCheck m) =>
        m.Status == IpqcCheckStatus.Ok
        || (m.Status == IpqcCheckStatus.Ng
            && m.DivergenceApprovalStatus == DivergenceApprovalStatus.Approved);

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
