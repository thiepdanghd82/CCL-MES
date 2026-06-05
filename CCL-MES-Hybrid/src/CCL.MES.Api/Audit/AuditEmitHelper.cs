using System.Text.Json;
using CCL.MES.Application.Audit;

namespace CCL.MES.Api.Audit;

/// <summary>
/// P10.7a-1 — enforces the required-key envelope on every MES audit
/// row per <c>docs/P10.7-WO-STATE-CONTRACT.md</c> §7.2. Replaces
/// inline <c>JsonSerializer.Serialize(new { ... })</c> at MES
/// callsites so the canonical keys (wo_id, wo_no, shift_code,
/// from_phase, to_phase, ok) cannot drift across PRs.
///
/// Sanitize whitelist (§7.3) is enforced upstream — callers pass
/// the extra-keys dictionary AFTER they have removed any sensitive
/// field. This helper does not pretend to scrub: by the time the
/// helper sees the dict, it is the caller's responsibility to have
/// dropped passwords / signature bytes / etc. Free-text reason
/// fields are length-capped at 500 characters server-side here.
/// </summary>
public static class AuditEmitHelper
{
    private const int ReasonFieldMaxLen = 500;

    private static readonly string[] ReasonLikeKeys =
    {
        "reason",
        "note",
        "comment",
        "special_accept_reason",
        "ng_note",
    };

    /// <summary>
    /// Build the canonical detail JSON string. Required keys are
    /// always emitted (null when not applicable to the event).
    /// Extra event-specific keys merge on top.
    /// </summary>
    /// <param name="woId">Work-order PK. Required for MES events.</param>
    /// <param name="woNo">Human-readable WO number (e.g. "WO-26-2852").</param>
    /// <param name="shiftCode">'A' / 'B' / 'C' from server-side
    /// derivation. Pass null only for non-shift events (admin
    /// recovery outside shift hours).</param>
    /// <param name="fromPhase">Canonical MesPhase string. Null for
    /// creation events.</param>
    /// <param name="toPhase">Canonical MesPhase string. Null for
    /// non-transition events (e.g. PREPRESS_MAT_OK).</param>
    /// <param name="ok">true if the event succeeded; false for
    /// conflict / reject rows.</param>
    /// <param name="extra">Event-specific keys per
    /// <c>P10.7-WO-STATE-CONTRACT.md</c> §7.2 table. Caller MUST
    /// have stripped sensitive fields per §7.3.</param>
    public static string BuildDetail(
        long woId,
        string woNo,
        string? shiftCode,
        string? fromPhase,
        string? toPhase,
        bool ok,
        IDictionary<string, object?>? extra = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["wo_id"] = woId,
            ["wo_no"] = woNo,
            ["shift_code"] = shiftCode,
            ["from_phase"] = fromPhase,
            ["to_phase"] = toPhase,
            ["ok"] = ok,
        };

        if (extra is not null)
        {
            foreach (var kv in extra)
            {
                // Cap reason-style free-text fields per §7.3
                // ("Length-limited to ≤ 500 chars; server enforces
                // truncation.").
                payload[kv.Key] = TruncateIfReasonLike(kv.Key, kv.Value);
            }
        }

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>
    /// Convenience wrapper that builds the detail JSON + emits
    /// directly through an <see cref="IAuditWriter"/>. Saves callers
    /// the two-line pattern at every transition site.
    /// </summary>
    public static Task EmitMesAsync(
        IAuditWriter audit,
        string action,
        string actor,
        string actorRole,
        long woId,
        string woNo,
        string? shiftCode,
        string? fromPhase,
        string? toPhase,
        bool ok,
        IDictionary<string, object?>? extra = null)
    {
        var detail = BuildDetail(woId, woNo, shiftCode, fromPhase, toPhase, ok, extra);
        return audit.EmitAsync(
            action: action,
            actor: actor,
            actorRole: actorRole,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: detail);
    }

    /// <summary>
    /// Compute the canonical shift code ('A' / 'B' / 'C') from a UTC
    /// timestamp + the CCL fixed 3-shift schedule per contract §4.4.
    /// Shift boundaries are Vietnam local time (UTC+7).
    /// Shift A: 06:00-14:00 / Shift B: 14:00-22:00 / Shift C: 22:00-06:00.
    /// </summary>
    public static string ComputeShiftCode(DateTime utcTimestamp)
    {
        // CCL Vietnam plant — UTC+7 fixed (no DST).
        var localHour = (utcTimestamp.AddHours(7).Hour);
        return localHour switch
        {
            >= 6 and < 14  => "A",
            >= 14 and < 22 => "B",
            _              => "C",
        };
    }

    private static object? TruncateIfReasonLike(string key, object? value)
    {
        if (value is not string s) return value;
        foreach (var rk in ReasonLikeKeys)
        {
            if (key.Equals(rk, StringComparison.OrdinalIgnoreCase))
            {
                return s.Length <= ReasonFieldMaxLen ? s : s[..ReasonFieldMaxLen];
            }
        }
        return value;
    }
}
