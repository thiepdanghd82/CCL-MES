namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7d-1 — configuration shape for the IPQC dual-signature gate
/// (Q3, Henry-flagged CRITICAL per the 7d scope proposal). Bound from
/// <c>appsettings.json</c> section <c>Features:IpqcRequireDistinctQaApprover</c>
/// or environment variable <c>OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER</c>
/// via <see cref="IpqcDualSigOptionsLoader"/>.
///
/// Default: <c>true</c> (4-eye QC enforced in production).
/// Override to <c>false</c> only in dev / UAT plants with a single
/// QC inspector. Must be documented in the plant's runbook per the
/// §5.5.1 contract clause.
/// </summary>
public sealed class IpqcDualSigOptions
{
    /// <summary>
    /// When <c>true</c> (default), <c>POST /wo/{id}/qa/approve</c>
    /// rejects with 422 + <c>qa.same_user_as_ipqc_submitter</c> +
    /// emits <c>WO_QA_APPROVE_DENIED</c> audit row when the
    /// authenticated user's username equals the row's
    /// <c>IpqcSubmittedBy</c>.
    /// </summary>
    public bool RequireDistinctQaApprover { get; init; } = true;

    /// <summary>
    /// Resolved flag state stamped into successful QA approval audit
    /// rows so forensic replay can distinguish "approved under
    /// dual-sig enforcement" from "approved under flag-OFF override".
    /// </summary>
    public string FlagState => RequireDistinctQaApprover ? "on" : "off";
}

/// <summary>
/// P10.7d-1 — parses environment + config into the
/// <see cref="IpqcDualSigOptions"/> shape with default-ON discipline.
/// Tested via 4 fixtures locking the parse rules per §5.5.1 contract:
/// stray typos default back to ON, explicit "false"/"0"/"off"/"no"
/// flip to OFF.
/// </summary>
public static class IpqcDualSigOptionsLoader
{
    /// <summary>
    /// Parse the env-var / config string into the bool. Returns the
    /// safe default (true) when the input is null/empty/unrecognised
    /// — that way a stray typo doesn't silently disable the gate.
    /// Only explicit OFF tokens (false/0/off/no, case-insensitive)
    /// flip the flag.
    /// </summary>
    public static bool ParseRequireDistinctQaApprover(string? raw)
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
