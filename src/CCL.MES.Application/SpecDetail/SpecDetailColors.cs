namespace CCL.MES.Application.SpecDetail;

/// <summary>
/// Phase 8 PR #31d — PANTONE swatch lookup port từ SpecHub
/// (spechub-prototype.html:10257-10261). Hard-coded mapping color name → hex
/// dùng cho silkscreen Print Process table swatch column.
///
/// `SwatchHex(colorName)` trả hex (e.g. "#FFE680") cho color name match,
/// null nếu không có. Caller render colored circle / square next to color
/// label. `IsTransparent` cho "CLEAR" colors → checker pattern overlay.
///
/// Future PR: load từ CCL color catalog table (DB-driven) thay vì hard-code.
/// </summary>
public static class SpecDetailColors
{
    private static readonly Dictionary<string, string> _swatches = new(StringComparer.OrdinalIgnoreCase)
    {
        // SpecHub HTML:10257-10261 — 9 colors most common silk samples
        { "WN-212",              "#FFE680" },
        { "WN-108",              "#FFB300" },
        { "PANTONE 186 C",       "#C8102E" },
        { "WN-366",              "#F4E03A" },
        { "VIC-120 CONC WHITE",  "#FFFFFF" },
        { "DENSE BLACK",         "#101820" },
        { "WN-341",              "#F3DA1F" },
        { "MEDIUM WHITE",        "#F5F5F5" },
        { "CLEAR",               "transparent" },
    };

    /// <summary>Trả hex color (e.g. "#FFE680") hoặc null nếu không match.</summary>
    public static string? SwatchHex(string? colorName)
    {
        if (string.IsNullOrWhiteSpace(colorName)) return null;
        return _swatches.TryGetValue(colorName.Trim(), out var hex) ? hex : null;
    }

    /// <summary>True nếu swatch là transparent (CLEAR) → render checker pattern.</summary>
    public static bool IsTransparent(string? colorName)
        => string.Equals(SwatchHex(colorName), "transparent", StringComparison.OrdinalIgnoreCase);
}
