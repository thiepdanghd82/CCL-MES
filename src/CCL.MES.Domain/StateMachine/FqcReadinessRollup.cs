using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// P10.7e-1 Q3+Q7 — pure helper computing the FQC review readiness
/// gate. Mirrors the 7d <see cref="IpqcReadinessRollup"/> shape so the
/// FqcDashboard's "submit judgment" button can be enabled/disabled
/// deterministically.
///
/// Unlike the 7d hardcoded 4-slot IPQC, FQC items come from the
/// profile snapshot (Q3 data-driven schema). The rollup counts the
/// child <see cref="WoQcCheckItem"/> rows by status — operators must
/// resolve EVERY item (Ok or Ng) before the Pass/Reject judgment
/// buttons enable.
///
/// Rollup contract:
///   - <c>IsReadyForJudgment</c> = every child item is non-Pending
///     AND the parent has at least one item (the empty snapshot
///     case is treated as "not ready" so admins notice an empty
///     profile config instead of silently auto-passing).
///   - <c>AllOk</c> = every item is Ok. Judgment defaults to Pass.
///     OQC reviewers still need 3 distinct signatures (see Q5).
///   - <c>AnyNg</c> = at least one item is Ng. Pass STILL allowed
///     (FQC uses sample-based AQL — operator may flag minor NGs
///     without failing the lot; SpecHub MES_FQC_PROFILE allows AQL
///     II 0.4 sampling). Reject available; required reason on
///     either path lands at the controller layer.
///
/// FQC vs OQC: this helper covers BOTH kinds since the math is
/// identical — only the parent <see cref="WoQcCheck.QcKind"/>
/// discriminator changes. The 3-sig rules for OQC live in the
/// controller per Q5.
/// </summary>
public static class WoQcReadinessRollup
{
    /// <summary>
    /// Compute the readiness rollup from a parent <see cref="WoQcCheck"/>
    /// + its child items. Caller passes null when no row exists yet
    /// (lazy-materialise case — caller treats this as "not ready",
    /// same semantics as "all items Pending").
    /// </summary>
    /// <param name="check">The check row (with Items loaded).</param>
    /// <param name="profileExpectedItemCount">P10.7e-3 FIX — number of
    /// items declared in the profile snapshot. When positive, readiness
    /// requires every profile item to have a non-Pending row (operator
    /// must touch all 12 FQC items / 28 OQC items). When 0 (legacy
    /// callsites or empty profile), falls back to the row-only count
    /// for backward compatibility.</param>
    public static (bool IsReadyForJudgment, bool AllOk, bool AnyNg) Compute(
        WoQcCheck? check,
        int profileExpectedItemCount = 0)
    {
        if (check is null) return (false, false, false);
        var items = check.Items;
        if (items.Count == 0) return (false, false, false);

        var allNonPending = true;
        var allOk = true;
        var anyNg = false;
        var nonPendingCount = 0;
        foreach (var it in items)
        {
            if (it.Status == IpqcCheckStatus.Pending)
            {
                allNonPending = false;
                allOk = false;
                continue;
            }
            nonPendingCount++;
            if (it.Status == IpqcCheckStatus.Ng)
            {
                anyNg = true;
                allOk = false;
            }
        }
        // P10.7e-3 FIX — when the caller knows the profile-expected count,
        // require the operator to have touched every profile item.
        // Otherwise: e.g. operator marked 5 of 12 items Ok + 7 untouched
        // = no persisted Pending rows but still 7 items unanswered →
        // judgment must stay gated. The legacy row-only rule
        // (allNonPending) silently passed this case before the fix.
        var ready = profileExpectedItemCount > 0
            ? (nonPendingCount >= profileExpectedItemCount && allNonPending)
            : allNonPending;
        return (ready, allOk, anyNg);
    }

    /// <summary>
    /// Q3+Q5 — FQC Pass + OQC Pass invariants share the rollup. OQC
    /// adds the 3-sig requirement which is NOT computed here (the
    /// rollup is data-only; signature flags + distinct-user invariants
    /// live in the controller alongside the
    /// <see cref="Application.Services.WoQcSigPolicyOptions"/> flags).
    /// </summary>
    public static bool IsJudgmentConsistent(WoQcCheck? check, WoQcJudgment intended)
    {
        if (check is null) return false;
        var (ready, _, _) = Compute(check);
        if (!ready) return false;
        // FQC + OQC: both Pass and Reject are valid choices when
        // ready — there's no GoRun-style "blocked when AnyNg" rule
        // because sample-based AQL allows minor NGs to coexist with
        // a Pass judgment (operator notes the AQL bucket; statistical
        // sample size honoured by the profile).
        return intended is WoQcJudgment.Pass or WoQcJudgment.Reject;
    }
}
