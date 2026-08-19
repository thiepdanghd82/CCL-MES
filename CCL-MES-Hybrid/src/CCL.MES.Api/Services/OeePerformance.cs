namespace CCL.MES.Api.Services;

/// <summary>
/// Đợt 1 C3 — single source of truth for the OEE *performance* leg.
///
/// Canonical speed source is <c>WorkCenter.IdealSpeedPcsH</c> (pieces per
/// hour, operator-editable on the NPI Work Centers grid). The ideal cycle
/// time is derived, never stored:
///
///     idealCycleSec = 3600.0 / IdealSpeedPcsH
///     plannedUnits  = runSeconds / idealCycleSec
///     performance   = qtyDone / plannedUnits
///
/// <c>Machine.IdealCycleTimeSec</c> is NOT a valid input here. It was a
/// second, divergent speed source that produced a different performance
/// figure for the same WO depending on which endpoint you asked, and it is
/// unpopulated for the codes the shop floor actually runs.
/// <c>scripts/gate-oee-single-source.sh</c> hard-fails on any reappearance.
///
/// The second half of this type is the reason code. Only 5 of 43 work
/// centers currently carry a speed, so most WOs legitimately have no
/// performance figure — but returning a bare <c>null</c> made that
/// indistinguishable from "computed and it happens to be nothing". 19 of
/// 27 WOs lost the metric silently. Every null now travels with the
/// machine-readable reason it is null; the client maps the code to a
/// localized sentence via <c>TranslationCatalog</c>.
/// </summary>
public static class OeePerformance
{
    /// <summary>WO carries no machine code, or no <c>WorkCenter</c> row
    /// matches it. Master-data gap upstream of speed.</summary>
    public const string ReasonWorkCenterMissing = "workcenter_missing";

    /// <summary>Work center resolved but <c>IdealSpeedPcsH</c> is null or
    /// ≤ 0. The common case today (38 of 43 work centers).</summary>
    public const string ReasonWorkCenterSpeedMissing = "workcenter_speed_missing";

    /// <summary>Operator never started, so there is no runtime to divide
    /// by. Not a data defect — the WO simply has not run yet.</summary>
    public const string ReasonNoRuntime = "no_runtime";

    /// <summary>Performance plus the reason it is absent. Exactly one of
    /// the two is non-null; the pair is never (null, null).</summary>
    public readonly record struct Result(double? Performance, string? UnavailableReason);

    /// <param name="workCenterResolved">true when a <c>WorkCenter</c> row
    /// was found for the WO's machine code.</param>
    /// <param name="idealSpeedPcsH"><c>WorkCenter.IdealSpeedPcsH</c>.</param>
    /// <param name="runSeconds">Net run seconds (pauses excluded).</param>
    /// <param name="qtyDone">Units produced, good + NG.</param>
    public static Result Compute(
        bool workCenterResolved,
        double? idealSpeedPcsH,
        long runSeconds,
        int qtyDone)
    {
        if (!workCenterResolved)
            return new Result(null, ReasonWorkCenterMissing);

        if (idealSpeedPcsH is not double speed || speed <= 0)
            return new Result(null, ReasonWorkCenterSpeedMissing);

        if (runSeconds <= 0)
            return new Result(null, ReasonNoRuntime);

        var idealCycleSec = 3600.0 / speed;
        var plannedUnits = runSeconds / idealCycleSec;
        if (plannedUnits <= 0)
            return new Result(null, ReasonNoRuntime);

        return new Result(qtyDone / plannedUnits, null);
    }
}
