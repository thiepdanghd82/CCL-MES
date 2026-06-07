using System.Text.Json;

namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7e-1 Q4 — pure helper for the 3-level per-item threshold
/// resolution chain. Mirrors the SpecHub MES_QUALITY_REDESIGN_PLAN.md
/// per-product override design (CCL Vietnam has ~10 customer-specific
/// overrides total; threshold lives in JSON keyed by item key).
///
/// Resolution chain (highest wins; null skips to next level):
///   1. <paramref name="productOverrideJson"/> [itemKey].threshold
///      — Product.QcProfileOverride row (Q4 column on Product entity).
///   2. <paramref name="profileSnapshotJson"/> [itemKey].threshold
///      — WoQcCheck.ProfileSnapshotJson (frozen at check-creation time
///      per Q3, so profile edits don't retroactively change rows
///      already in flight).
///   3. <paramref name="hardcodedDefault"/> — item-definition default.
///      The controller layer supplies this from a static constant
///      table (analogous to the 7d IPQC ΔE ≤ 2 default).
///
/// Single static surface so it can be reused by both the FQC + OQC
/// controllers in 7e-2 without DI ceremony. JSON parsing is
/// exception-tolerant — malformed override / snapshot strings fall
/// through to the next level rather than throw (operator never sees
/// a 500 because someone hand-edited the JSON wrong).
/// </summary>
public static class QcThresholdResolver
{
    /// <summary>
    /// Resolve the effective threshold for a single item. Returns the
    /// hardcoded default when override + profile both miss.
    ///
    /// JSON shape expected at both override + snapshot:
    /// <code>
    ///   { "&lt;itemKey&gt;": { "threshold": &lt;number&gt;, "enabled": true|false }, ... }
    /// </code>
    /// </summary>
    /// <param name="itemKey">Item key to resolve (case-sensitive).</param>
    /// <param name="productOverrideJson">Product.QcProfileOverride column value; null when unset.</param>
    /// <param name="profileSnapshotJson">WoQcCheck.ProfileSnapshotJson value; should always be non-null in practice but tolerant of null for unit tests.</param>
    /// <param name="hardcodedDefault">Last-resort default (item-definition).</param>
    public static double Resolve(
        string itemKey,
        string? productOverrideJson,
        string? profileSnapshotJson,
        double hardcodedDefault)
    {
        // Level 1 — Product override (highest priority).
        if (TryReadThreshold(productOverrideJson, itemKey, out var overrideValue))
            return overrideValue;

        // Level 2 — Profile snapshot.
        if (TryReadThreshold(profileSnapshotJson, itemKey, out var profileValue))
            return profileValue;

        // Level 3 — Hardcoded default.
        return hardcodedDefault;
    }

    /// <summary>
    /// Item-enable resolver. Defaults to TRUE when no level explicitly
    /// disables — same precedence chain as <see cref="Resolve"/>. Lets a
    /// product override DISABLE an item (eg. customer X doesn't require
    /// the colour check on a specific SKU) without touching the
    /// underlying process profile.
    /// </summary>
    public static bool IsEnabled(
        string itemKey,
        string? productOverrideJson,
        string? profileSnapshotJson)
    {
        if (TryReadEnabled(productOverrideJson, itemKey, out var overrideEnabled))
            return overrideEnabled;
        if (TryReadEnabled(profileSnapshotJson, itemKey, out var profileEnabled))
            return profileEnabled;
        return true;
    }

    // ── Internal JSON readers ──────────────────────────────────────

    private static bool TryReadThreshold(string? json, string itemKey, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty(itemKey, out var item)) return false;
            if (item.ValueKind != JsonValueKind.Object) return false;
            if (!item.TryGetProperty("threshold", out var t)) return false;
            if (t.ValueKind != JsonValueKind.Number) return false;
            value = t.GetDouble();
            return true;
        }
        catch (JsonException)
        {
            // Malformed JSON — fall through to next level. The operator
            // sees the next-level value rather than a 500; ops can fix
            // the override via SQL when they notice.
            return false;
        }
    }

    private static bool TryReadEnabled(string? json, string itemKey, out bool value)
    {
        value = true;
        if (string.IsNullOrWhiteSpace(json)) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            if (!doc.RootElement.TryGetProperty(itemKey, out var item)) return false;
            if (item.ValueKind != JsonValueKind.Object) return false;
            if (!item.TryGetProperty("enabled", out var e)) return false;
            if (e.ValueKind == JsonValueKind.True)  { value = true;  return true; }
            if (e.ValueKind == JsonValueKind.False) { value = false; return true; }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
