using CCL.MES.Shared.Prepress;

namespace CCL.MES.Hybrid.Client.Prepress;

/// <summary>Outcome of matching a scanned material barcode to a WO's BOM.</summary>
public enum MaterialMatchOutcome
{
    /// <summary>Scanned payload was empty / no part number could be read.</summary>
    EmptyCode,
    /// <summary>Part number not present in this WO's BOM.</summary>
    NoMatch,
    /// <summary>Exactly one BOM row matched — safe to auto-record OK.</summary>
    Single,
    /// <summary>Part number appears on more than one BOM line — operator picks manually.</summary>
    Multiple,
}

/// <summary>Result of <see cref="MaterialBarcodeMatcher.Match"/>.</summary>
/// <param name="Outcome">Match classification.</param>
/// <param name="Row">The matched row when <see cref="MaterialMatchOutcome.Single"/>; otherwise null.</param>
/// <param name="PartNo">The part number extracted from the scan (for operator-facing messages).</param>
public readonly record struct MaterialMatchResult(
    MaterialMatchOutcome Outcome,
    PrepressMaterialRow? Row,
    string PartNo);

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

    /// <summary>Match a scan against this WO's BOM rows (exact part number only).</summary>
    public static MaterialMatchResult Match(
        IReadOnlyList<PrepressMaterialRow> materials, string? scannedCode)
    {
        var partNo = ExtractPartNo(scannedCode);
        if (partNo.Length == 0)
            return new MaterialMatchResult(MaterialMatchOutcome.EmptyCode, null, string.Empty);

        var hits = materials
            .Where(m => string.Equals(
                (m.MaterialCode ?? string.Empty).Trim(),
                partNo,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return hits.Count switch
        {
            0 => new MaterialMatchResult(MaterialMatchOutcome.NoMatch, null, partNo),
            1 => new MaterialMatchResult(MaterialMatchOutcome.Single, hits[0], partNo),
            _ => new MaterialMatchResult(MaterialMatchOutcome.Multiple, null, partNo),
        };
    }
}
