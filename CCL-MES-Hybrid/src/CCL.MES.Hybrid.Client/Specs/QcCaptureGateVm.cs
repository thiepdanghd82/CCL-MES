using CCL.MES.Shared.QcSpecs;

namespace CCL.MES.Hybrid.Client.Specs;

/// <summary>
/// P10.5f — Pure-helper VM for the QC Capture modal. Mirrors the
/// server-side guards in <c>SpecQcCaptureService.CaptureAsync</c>
/// (FAIL → reason required) + the role gate on
/// <c>SpecQcWindowService</c>/<c>SpecQcCaptureService</c>
/// (`_editorRoles` = Admin / Engineer).
///
/// The server is the source of truth — every guard re-runs on the
/// orchestrator. Mirroring here is purely a UX optimisation so the
/// operator can't tap Save with an obviously-invalid combination.
/// </summary>
public static class QcCaptureGateVm
{
    /// <summary>Roles allowed to edit QC plans + capture results.
    /// Mirror of <c>SpecQcWindowService._editorRoles</c> +
    /// <c>SpecQcCaptureService._editorRoles</c>. Admin + Engineer.</summary>
    public static bool IsEditor(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Engineer", StringComparison.OrdinalIgnoreCase);

    /// <summary>FAIL requires a non-empty reason code referencing an
    /// active Scrap-kind <see cref="QcReasonCode"/>. PASS / Na allow
    /// null reason. Returns true when the combination is acceptable
    /// at the client layer.</summary>
    public static bool IsCaptureValid(QcCaptureResult result, string? ngReasonCode) =>
        result switch
        {
            QcCaptureResult.Fail => !string.IsNullOrWhiteSpace(ngReasonCode),
            _                    => true,
        };

    /// <summary>VN reason message for the UI when the capture is not
    /// valid yet. Used as the disabled-button tooltip + inline hint
    /// under the comment textarea.</summary>
    public static string? ExplainInvalid(QcCaptureResult result, string? ngReasonCode)
    {
        if (IsCaptureValid(result, ngReasonCode)) return null;
        return result == QcCaptureResult.Fail
            ? "You must select a reason code when the result is FAIL."
            : "The data is incomplete.";
    }

    /// <summary>Filter the full active reason-code list to the subset
    /// the FAIL dropdown should show — Scrap kind only. PASS / Na
    /// don't need the dropdown so callers can skip the filter when
    /// the result is non-FAIL.</summary>
    public static IReadOnlyList<QcReasonCode> ScrapKindOnly(IEnumerable<QcReasonCode> all) =>
        all.Where(r => string.Equals(r.Kind, "Scrap", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.Sort)
            .ThenBy(r => r.Code, StringComparer.Ordinal)
            .ToList();

    /// <summary>Pick the latest capture per criterion id from a flat
    /// list of all captures on the revision. Server returns CapturedAt
    /// ASC so the last entry per key wins. Returns a dictionary keyed
    /// by criterion id; criteria with no captures are absent.</summary>
    public static Dictionary<long, QcCaptureItem> LatestPerCriterion(IEnumerable<QcCaptureItem> all)
    {
        var dict = new Dictionary<long, QcCaptureItem>();
        foreach (var c in all)
        {
            // Server returns ASC; last seen wins → latest survives.
            dict[c.QcCriterionId] = c;
        }
        return dict;
    }
}
