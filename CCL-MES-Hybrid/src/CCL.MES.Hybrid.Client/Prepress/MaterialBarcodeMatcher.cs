using CCL.MES.Shared.Prepress;

namespace CCL.MES.Hybrid.Client.Prepress;

/// <summary>Outcome of matching a scanned material barcode to a WO's BOM.</summary>
public enum MaterialMatchOutcome
{
    /// <summary>Scanned payload was empty / no part number could be read.</summary>
    EmptyCode,
    /// <summary>Part number not present in this WO's BOM.</summary>
    NoMatch,
    /// <summary>A BOM row was selected to confirm (first not-yet-OK line of that part).</summary>
    Single,
    /// <summary>Every BOM line for that part number is already OK.</summary>
    AllOk,
}

/// <summary>Result of <see cref="MaterialBarcodeMatcher.Match"/>.</summary>
/// <param name="Outcome">Match classification.</param>
/// <param name="Row">The row to confirm when <see cref="MaterialMatchOutcome.Single"/>; otherwise null.</param>
/// <param name="PartNo">The part number extracted from the scan (for the Part Scan column + messages).</param>
/// <param name="Description">The descriptive remainder of the scan (for the Part Description column).</param>
public readonly record struct MaterialMatchResult(
    MaterialMatchOutcome Outcome,
    PrepressMaterialRow? Row,
    string PartNo,
    string Description);

/// <summary>
/// Pure matcher: scanned material label → the WO's BOM row to confirm.
///
/// <para>
/// Label format verified against <c>Data/Raw Materials.xlsx</c> + 3 live
/// scans. A label reads <c>&lt;partNo&gt;/&lt;n&gt;/(&lt;spec&gt;) / &lt;desc&gt; (&lt;dims&gt;)</c>
/// — the part number is the substring before the FIRST <c>'/'</c>. Examples:
/// </para>
/// <list type="bullet">
///   <item><c>30030532/50/(LU29) / lukitape #. 1407V1 (245mm x 50M)</c> → <c>30030532</c></item>
///   <item><c>30031701-0228/250/(SDT (EA)#1R(K241221)) 228mm x 1000M</c> → <c>30031701-0228</c></item>
///   <item><c>30031145/80/(BU'488) / BU'-0112N (215mm x 1000M)</c> → <c>30031145</c></item>
/// </list>
///
/// <para>
/// <b>EXACT match only — no suffix stripping.</b> The Raw Materials master
/// holds <c>30031701</c> (numeric), <c>30031701-0228</c> (text) AND
/// <c>30031702</c> as three DISTINCT catalog parts. Stripping the
/// <c>-0228</c> suffix (or collapsing to the leading digits) would silently
/// confirm the WRONG material, so we match the full part number verbatim
/// (trimmed, case-insensitive). A miss is a miss — the operator records by
/// hand. Match is on the part NUMBER only, never the description (scan OCR
/// is noisy: "BW488" reads as "BU'488").
/// </para>
/// </summary>
public static class MaterialBarcodeMatcher
{
    /// <summary>Part number = substring before the first '/', trimmed.</summary>
    public static string ExtractPartNo(string? scannedCode)
    {
        var s = (scannedCode ?? string.Empty).Trim();
        if (s.Length == 0) return string.Empty;
        var slash = s.IndexOf('/');
        return (slash >= 0 ? s[..slash] : s).Trim();
    }

    /// <summary>Descriptive remainder = everything after the first '/', trimmed
    /// (the label text the operator eyeballs against the BOM Description).</summary>
    public static string ExtractDescription(string? scannedCode)
    {
        var s = (scannedCode ?? string.Empty).Trim();
        var slash = s.IndexOf('/');
        return slash >= 0 && slash + 1 < s.Length ? s[(slash + 1)..].Trim() : string.Empty;
    }

    /// <summary>Part Description = the description of the MATCHED BOM row, NOT
    /// the noisy remainder of the scan. A bare code (e.g. "30030491-0145" with
    /// no "/desc") has no scan-remainder, so <see cref="ExtractDescription"/>
    /// would yield "" → "—". Once a row is matched we already KNOW the real
    /// component description; use it. Fall back to the scan remainder only when
    /// the row has no description (legacy IFS rows) but the scan carried one.</summary>
    public static string ResolveDescription(PrepressMaterialRow? row, string? scannedCode)
    {
        if (row is not null && !string.IsNullOrWhiteSpace(row.MaterialDescription))
            return row.MaterialDescription!.Trim();
        return ExtractDescription(scannedCode);
    }

    /// <summary>
    /// Pick the BOM row a scan should confirm. Exact part-number match only.
    /// When a part appears on several BOM lines (e.g. the same component on
    /// two lines), each scan confirms the FIRST line not yet OK — so scanning
    /// the part N times confirms the N lines in order. If every matching line
    /// is already OK → <see cref="MaterialMatchOutcome.AllOk"/>.
    /// </summary>
    public static MaterialMatchResult Match(
        IReadOnlyList<PrepressMaterialRow> materials, string? scannedCode)
    {
        var partNo = ExtractPartNo(scannedCode);
        var description = ExtractDescription(scannedCode);
        if (partNo.Length == 0)
            return new MaterialMatchResult(MaterialMatchOutcome.EmptyCode, null, string.Empty, description);

        var hits = materials
            .Where(m => string.Equals(
                (m.MaterialCode ?? string.Empty).Trim(),
                partNo,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (hits.Count == 0)
            return new MaterialMatchResult(MaterialMatchOutcome.NoMatch, null, partNo, description);

        var target = hits.FirstOrDefault(m =>
            !string.Equals(m.Status, "Ok", StringComparison.OrdinalIgnoreCase));

        // Description now resolves from the matched BOM row (real component
        // description), falling back to the scan remainder only for a legacy
        // row without one — so a bare code still populates Part Description.
        return target is not null
            ? new MaterialMatchResult(MaterialMatchOutcome.Single, target, partNo, ResolveDescription(target, scannedCode))
            : new MaterialMatchResult(MaterialMatchOutcome.AllOk, hits[0], partNo, ResolveDescription(hits[0], scannedCode));
    }
}
