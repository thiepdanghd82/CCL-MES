namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7e-1 Q5 — OQC 3-signature policy options. Generalises the
/// 7d-1 <see cref="IpqcDualSigOptions"/> pattern to three independent
/// distinct-user invariants on the outgoing-quality check
/// (Inspector → Reviewer → Approver, per the physical CCL-10-F6
/// form).
///
/// Bound from <c>appsettings.json</c> section
/// <c>Features:WoQcSigPolicy</c> or 3 environment variables
/// (<c>OPS_OQC_REQUIRE_DISTINCT_REVIEWER</c>,
/// <c>OPS_OQC_REQUIRE_DISTINCT_APPROVER</c>,
/// <c>OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR</c>)
/// via <see cref="WoQcSigPolicyOptionsLoader"/>.
///
/// All 3 flags default to <c>true</c> per Lesson L20 (Q3 default-ON
/// + typo-safe whitelist parse + boot-probe log line). Override to
/// <c>false</c> only in dev / single-inspector UAT plants — must be
/// documented in the plant's runbook (mirrors the §5.5.1 IPQC
/// dual-sig clause).
///
/// Server-side enforcement (in OqcReviewController, lands 7e-2):
///   • Reviewer ≠ Inspector → 422 oqc.same_user_as_inspector +
///     WO_OQC_REVIEW_DENIED audit
///   • Approver ≠ Reviewer → 422 oqc.same_user_as_reviewer +
///     WO_OQC_APPROVE_DENIED audit
///   • Approver ≠ Inspector → 422 oqc.same_user_as_inspector +
///     WO_OQC_APPROVE_DENIED audit
/// </summary>
public sealed class WoQcSigPolicyOptions
{
    /// <summary>
    /// When <c>true</c> (default), Reviewer tap rejects when
    /// authenticated username equals row's <c>OqcInspectedBy</c>.
    /// </summary>
    public bool OqcRequireDistinctReviewer { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), Approver tap rejects when
    /// authenticated username equals row's <c>OqcReviewedBy</c>.
    /// </summary>
    public bool OqcRequireDistinctApprover { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), Approver tap ALSO rejects when
    /// authenticated username equals row's <c>OqcInspectedBy</c>.
    /// Together with <see cref="OqcRequireDistinctApprover"/> this
    /// closes the "Approver = Inspector but ≠ Reviewer" loophole.
    /// </summary>
    public bool OqcRequireApproverDistinctFromInspector { get; set; } = true;

    /// <summary>
    /// Resolved flag state triple stamped into successful OQC audit
    /// rows so forensic replay can distinguish "approved under full
    /// 3-sig enforcement" from "approved with one of the flags off".
    /// Format: <c>R={on|off};A={on|off};AI={on|off}</c>.
    /// </summary>
    public string FlagState =>
        $"R={(OqcRequireDistinctReviewer ? "on" : "off")};" +
        $"A={(OqcRequireDistinctApprover ? "on" : "off")};" +
        $"AI={(OqcRequireApproverDistinctFromInspector ? "on" : "off")}";

    /// <summary>
    /// True iff ALL 3 flags are ON — convenient for the boot-probe
    /// log line that summarises the policy in one token.
    /// </summary>
    public bool AllFlagsOn =>
        OqcRequireDistinctReviewer
        && OqcRequireDistinctApprover
        && OqcRequireApproverDistinctFromInspector;
}

/// <summary>
/// P10.7e-1 Q5 — parses environment + config into the
/// <see cref="WoQcSigPolicyOptions"/> shape with default-ON discipline.
/// Reuses verbatim the L20-codified whitelist-parse pattern from
/// <see cref="IpqcDualSigOptionsLoader"/>: only explicit OFF tokens
/// (false/0/off/no, case-insensitive, trimmed) flip a flag;
/// everything else (null/empty/typos/uppercase variants/extra
/// whitespace) keeps the flag ON.
/// </summary>
public static class WoQcSigPolicyOptionsLoader
{
    /// <summary>
    /// Parse a single env-var / config string into a bool. Returns
    /// the safe default (true) when the input is null/empty/
    /// unrecognised — that way a stray typo doesn't silently disable
    /// any of the 3 sig invariants. Only explicit OFF tokens
    /// (false/0/off/no, case-insensitive) flip the flag.
    /// </summary>
    public static bool ParseFlag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var s = raw.Trim().ToLowerInvariant();
        return s switch
        {
            "false" or "0" or "off" or "no" => false,
            _                                => true,
        };
    }
}
