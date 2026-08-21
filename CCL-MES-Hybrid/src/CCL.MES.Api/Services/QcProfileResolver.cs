using System.Text.Json;
using CCL.MES.Application.Services;

namespace CCL.MES.Api.Services;

/// <summary>
/// Pure profile/threshold JSON logic extracted from
/// <c>WoQcReviewController</c> (A2 thin-controller, L47). No DbContext,
/// no HttpContext — every method is unit-testable in isolation.
///
/// Behaviour is preserved verbatim from the former private controller
/// members:
///   • <see cref="TryExtractKindFromOverride"/> tolerates two override
///     shapes (per-kind map + direct profile) and falls through silently
///     on malformed JSON.
///   • <see cref="ExtractProfileItemKeys"/> / <see cref="ProfileKeyCount"/>
///     read the <c>sections[*].items[*].key</c> chain; "{}"/null/malformed
///     yield an empty list.
///   • <see cref="ResolveSnapshot"/> is the pure form of the Q4 3-level
///     chain: L1 per-product override → L2 <see cref="QcProfileSeed"/>
///     system default → L3 "{}". The DB read for the override JSON stays
///     in the controller; this class owns only the pure resolution.
/// </summary>
public static class QcProfileResolver
{
    /// <summary>P10.7e-3 Q4 3-level profile resolution chain (pure form).
    ///   L1: per-product override JSON (<paramref name="overrideJson"/>);
    ///   L2: <see cref="QcProfileSeed.GetDefaultProfileJson(string)"/>;
    ///   L3: "{}" empty (only when both levels miss).
    /// The controller reads <paramref name="overrideJson"/> from
    /// <c>_db.Products</c> and passes it in — no DB coupling here.</summary>
    public static string ResolveSnapshot(string? overrideJson, string kind)
    {
        // L1 — per-product override.
        if (TryExtractKindFromOverride(overrideJson, kind, out var extracted))
            return extracted;
        // L2 — system default.
        var seeded = QcProfileSeed.GetDefaultProfileJson(kind);
        if (!string.IsNullOrEmpty(seeded)) return seeded;
        // L3 — empty.
        return "{}";
    }

    /// <summary>Product.QcProfileOverride may carry per-kind overrides
    /// keyed by "fqc"/"oqc" OR be a single profile snapshot.
    /// Tolerant of both shapes; falls through silently on malformed JSON.</summary>
    public static bool TryExtractKindFromOverride(string? overrideJson, string kind, out string extracted)
    {
        extracted = "";
        if (string.IsNullOrWhiteSpace(overrideJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(overrideJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            // Shape 1: { "fqc": {...}, "oqc": {...} }
            var kindLower = kind.ToLowerInvariant();
            if (doc.RootElement.TryGetProperty(kindLower, out var perKind)
                && perKind.ValueKind == JsonValueKind.Object)
            {
                extracted = perKind.GetRawText();
                return true;
            }
            // Shape 2: direct profile shape { "sections": [...] } — accept only
            // when the override declares kind via a top-level "kind" field.
            if (doc.RootElement.TryGetProperty("kind", out var k)
                && k.ValueKind == JsonValueKind.String
                && string.Equals(k.GetString(), kind, StringComparison.OrdinalIgnoreCase)
                && doc.RootElement.TryGetProperty("sections", out _))
            {
                extracted = overrideJson;
                return true;
            }
        }
        catch (JsonException) { /* malformed override — fall through */ }
        return false;
    }

    /// <summary>Extracts the ordered list of item keys declared by the
    /// profile snapshot's sections[*].items[*].key chain. Returns empty
    /// when the snapshot is "{}" or malformed.</summary>
    public static IReadOnlyList<string> ExtractProfileItemKeys(string? profileSnapshotJson)
    {
        if (string.IsNullOrWhiteSpace(profileSnapshotJson) || profileSnapshotJson == "{}")
            return new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(profileSnapshotJson);
            if (!doc.RootElement.TryGetProperty("sections", out var sections))
                return new List<string>();
            var keys = new List<string>();
            foreach (var section in sections.EnumerateArray())
            {
                if (!section.TryGetProperty("items", out var items)) continue;
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("key", out var k)
                        && k.ValueKind == JsonValueKind.String)
                    {
                        var key = k.GetString();
                        if (!string.IsNullOrEmpty(key)) keys.Add(key);
                    }
                }
            }
            return keys;
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    public static int ProfileKeyCount(string? profileSnapshotJson)
        => ExtractProfileItemKeys(profileSnapshotJson).Count;
}
