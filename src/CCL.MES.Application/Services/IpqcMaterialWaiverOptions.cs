namespace CCL.MES.Application.Services;

/// <summary>
/// IPQC first-article — configuration shape for the MATERIAL (SYSTEM)
/// divergence-waiver dual-signature gate (Henry 2026-08-25, Q1 soft-lock).
/// Bound from env <c>OPS_IPQC_REQUIRE_DISTINCT_MATERIAL_WAIVER</c> or config key
/// <c>Features:IpqcRequireDistinctMaterialWaiver</c> via
/// <see cref="IpqcMaterialWaiverOptionsLoader"/>. Mirrors
/// <see cref="IpqcDualSigOptions"/> verbatim (default-ON discipline per L20).
///
/// Default <c>true</c>: the Engineer who waives a divergence must NOT be the
/// same person who confirmed the MATERIAL row (4-eye). Override to false only
/// in single-inspector dev/UAT plants, documented in the runbook.
/// </summary>
public sealed class IpqcMaterialWaiverOptions
{
    /// <summary>When <c>true</c> (default), <c>POST …/material-system/{idx}/
    /// approve-divergence</c> rejects with 422 <c>material.same_user_as_confirmer</c>
    /// + emits <c>WO_IPQC_MATERIAL_APPROVE_DENIED</c> when the approver equals the
    /// row's <c>ConfirmedBy</c>.</summary>
    public bool RequireDistinctMaterialWaiver { get; set; } = true;

    /// <summary>Resolved flag state stamped into successful waiver audit rows.</summary>
    public string FlagState => RequireDistinctMaterialWaiver ? "on" : "off";
}

/// <summary>
/// IPQC first-article — parses env + config into <see cref="IpqcMaterialWaiverOptions"/>
/// with default-ON discipline (mirror <see cref="IpqcDualSigOptionsLoader"/>).
/// Only explicit OFF tokens (false/0/off/no) flip the flag; a stray typo stays ON.
/// </summary>
public static class IpqcMaterialWaiverOptionsLoader
{
    public static bool ParseRequireDistinctMaterialWaiver(string? raw)
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
