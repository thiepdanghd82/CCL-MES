using CCL.MES.Domain.Entities;

namespace CCL.MES.Domain.StateMachine;

/// <summary>
/// P10.7d-1 — pure helper computing the IPQC review readiness gate.
/// Mirrors the 7b <see cref="MaterialsReadinessRollup"/> shape so the
/// IPQC dashboard's "submit judgment" button can be enabled/disabled
/// deterministically.
///
/// Per SpecHub §3 line 138: *"Judgment — 3 nút lớn (bắt buộc 4 check
/// trên đã tick mới enable)"*. The 4 checks are Material recheck +
/// PrintA Color + PrintB Registration + PrintC Content. Each slot is
/// either Pending (operator hasn't touched) or Ok (operator confirmed)
/// or Ng (operator flagged + provided NG reason + note).
///
/// Rollup contract:
///   - <c>IsReadyForJudgment</c> = every slot is non-Pending (all 4
///     resolved either OK or NG).
///   - <c>AllOk</c> = every slot is Ok (Material + 3 prints all pass).
///     When true, judgment defaults to GoRun.
///   - <c>AnyNg</c> = at least one slot is Ng. Judgment must be
///     StopLine or SpecialAccept; GoRun is rejected at the controller
///     layer.
///
/// The check row's presence is itself a snapshot marker — pre-7d WOs
/// in IPQC_WAIT phase will have no <c>WoIpqcCheck</c> row until the
/// QC operator first opens the dashboard (lazy materialise per the
/// 7b BomSnapshot pattern).
/// </summary>
public static class IpqcReadinessRollup
{
    /// <summary>
    /// Compute the readiness rollup from a single <see cref="WoIpqcCheck"/>
    /// row. Caller passes null when no row exists yet (lazy-materialise
    /// case — caller treats this as "not ready", same semantics as
    /// "all 4 slots Pending").
    /// </summary>
    public static (bool IsReadyForJudgment, bool AllOk, bool AnyNg) Compute(
        WoIpqcCheck? check)
    {
        if (check is null) return (false, false, false);

        var slots = new[]
        {
            check.MaterialStatus,
            check.PrintAStatus,
            check.PrintBStatus,
            check.PrintCStatus,
        };

        var ready = slots.All(s => s != IpqcCheckStatus.Pending);
        var allOk = slots.All(s => s == IpqcCheckStatus.Ok);
        var anyNg = slots.Any(s => s == IpqcCheckStatus.Ng);

        return (ready, allOk, anyNg);
    }

    /// <summary>
    /// Convenience: is the given judgment <i>consistent</i> with the
    /// current slot results? Used by the controller to reject e.g. an
    /// operator submitting GoRun while one of the 4 slots is NG.
    /// Returns true when the judgment is internally consistent given
    /// the slot results; controller rejects with 422 when false.
    /// </summary>
    public static bool IsJudgmentConsistent(
        WoIpqcCheck? check, IpqcJudgment proposed)
    {
        var (ready, allOk, anyNg) = Compute(check);
        if (!ready) return false; // can't judge until 4 slots resolved
        return proposed switch
        {
            // GoRun requires all 4 OK. Any NG → must escalate or reset.
            IpqcJudgment.GoRun         => allOk,
            // StopLine is always legal when ready (NG → reset, OK → too
            // strict but operator may abort for any reason).
            IpqcJudgment.StopLine      => true,
            // SpecialAccept is only meaningful when at least one slot
            // is NG (otherwise GoRun is the right call).
            IpqcJudgment.SpecialAccept => anyNg,
            // Pending is not a submittable judgment.
            IpqcJudgment.Pending       => false,
            _                          => false,
        };
    }
}
