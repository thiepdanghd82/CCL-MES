namespace CCL.MES.Hybrid.Client.WorkOrders;

/// <summary>
/// P10.7a-1.3 — operator-input WO-code cleanup. Operators typing on
/// the Catalyst soft-keyboard introduce stray whitespace at both
/// ends + mixed case (autocaps kicks in unpredictably). The
/// normalizer is the single place that decides what string actually
/// hits the API, so the scan path + the manual-entry path can't
/// drift on character handling.
/// </summary>
public static class WoCodeNormalizer
{
    /// <summary>
    /// Normalise an operator-typed WO code:
    ///   - Trim leading + trailing whitespace.
    ///   - Strip embedded control characters (some IME keyboards send
    ///     U+200B zero-width space between segments).
    ///   - Reject the empty string by returning <c>null</c>.
    ///
    /// Does NOT uppercase — WO codes have mixed-case suffixes for
    /// some customers (e.g. <c>WO-26-3683a</c>). The server lookup
    /// is case-sensitive; the operator's job is to type what's on
    /// the label.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var trimmed = raw.Trim();
        if (trimmed.Length == 0) return null;
        // Strip zero-width + non-printable controls (Cc + Cf categories).
        var clean = new System.Text.StringBuilder(trimmed.Length);
        foreach (var ch in trimmed)
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == System.Globalization.UnicodeCategory.Control ||
                cat == System.Globalization.UnicodeCategory.Format)
                continue;
            clean.Append(ch);
        }
        var result = clean.ToString();
        return result.Length == 0 ? null : result;
    }

    /// <summary>
    /// Operator-facing minimum-viable validation: the normalised
    /// string must be at least 3 chars (shortest plausible WO code
    /// like <c>"W1"</c> is too short to be a real shop label).
    /// Returns the normalised code on success, <c>null</c> on
    /// rejection so the caller can show the Vietnamese banner.
    /// </summary>
    public static string? NormalizeForManualEntry(string? raw)
    {
        var norm = Normalize(raw);
        return norm is { Length: >= 3 } ? norm : null;
    }
}
