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
        // Mọi nhánh đi qua Enrich: đây là ĐIỂM THẮT DUY NHẤT mà snapshot được
        // đóng băng, nên nhét bản EN ở đây thì mọi đường freeze đều có cả hai
        // ngôn ngữ — kể cả override theo mã hàng. Xem QcProfileEnglish.
        // L1 — per-product override.
        if (TryExtractKindFromOverride(overrideJson, kind, out var extracted))
            return QcProfileEnglish.Enrich(extracted);
        // L2 — system default.
        var seeded = QcProfileSeed.GetDefaultProfileJson(kind);
        if (!string.IsNullOrEmpty(seeded)) return QcProfileEnglish.Enrich(seeded);
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
    /// <summary>Nhãn hiển thị của một hạng mục, đọc từ snapshot ĐÃ ĐÓNG BĂNG
    /// của chính check đó — không tra lại seed hiện hành. Sửa master data về
    /// sau KHÔNG đổi chữ trên hồ sơ đã ký.</summary>
    public readonly record struct ItemText(
        string? Label, string? LabelEn, string? Spec, string? SpecEn, string? Method, string? MethodEn);

    /// <summary>
    /// Bảng tra key → nhãn, dựng một lần cho mỗi lần render.
    ///
    /// <para>Trước đây DTO không mang nhãn ra nên UI render thẳng
    /// <c>ItemKey</c> — người vận hành nhìn thấy <c>color_match</c> thay vì
    /// "Màu sắc đúng mẫu chuẩn". Nhãn CÓ trong snapshot, chỉ là không ai đọc.</para>
    ///
    /// <para>JSON hỏng hoặc sai shape ⇒ trả bảng rỗng; phía gọi rơi về key.</para>
    /// </summary>
    public static IReadOnlyDictionary<string, ItemText> ExtractItemText(string? profileSnapshotJson)
    {
        var map = new Dictionary<string, ItemText>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(profileSnapshotJson) || profileSnapshotJson == "{}") return map;

        try
        {
            using var doc = JsonDocument.Parse(profileSnapshotJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return map;
            if (!doc.RootElement.TryGetProperty("sections", out var sections)
                || sections.ValueKind != JsonValueKind.Array) return map;

            foreach (var section in sections.EnumerateArray())
            {
                if (section.ValueKind != JsonValueKind.Object) continue;
                if (!section.TryGetProperty("items", out var items)
                    || items.ValueKind != JsonValueKind.Array) continue;

                foreach (var it in items.EnumerateArray())
                {
                    if (it.ValueKind != JsonValueKind.Object) continue;
                    var key = Str(it, "key");
                    if (string.IsNullOrWhiteSpace(key) || map.ContainsKey(key!)) continue;
                    map[key!] = new ItemText(
                        Str(it, "label"), Str(it, "label_en"),
                        Str(it, "spec"),  Str(it, "spec_en"),
                        Str(it, "method"), Str(it, "method_en"));
                }
            }
        }
        catch (JsonException) { /* snapshot hỏng — rơi về key, không làm vỡ trang */ }

        return map;

        static string? Str(JsonElement o, string name)
            => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
               && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()
                : null;
    }

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
