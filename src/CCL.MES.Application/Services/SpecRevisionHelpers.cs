using System.Globalization;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 8 PR-L1 — Helpers for revision code arithmetic. Lives in this
/// shared spot because PR-L1 (Copy) and PR-L2 (Revise) both need NextRev;
/// extracting once keeps the algorithm consistent across both ops.
///
/// Algorithm mirrors SpecHub `nextRev()` JS:
///   A → B, B → C, …, Y → Z, Z → AA, AZ → BA, ZZ → AAA, null/"" → A.
/// </summary>
public static class SpecRevisionHelpers
{
    /// <summary>
    /// Bump an alpha rev code by 1. Roll-overs handled (Z → AA, ZZ → AAA).
    /// Null/empty/whitespace input becomes "A". Input is upper-cased before
    /// processing; callers should already store in canonical form.
    /// </summary>
    public static string NextRev(string? current)
    {
        var r = (current ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(r)) return "A";
        var chars = r.ToCharArray();
        int i = chars.Length - 1;
        while (i >= 0)
        {
            if (chars[i] == 'Z') { chars[i] = 'A'; i--; }
            else { chars[i] = (char)(chars[i] + 1); return new string(chars); }
        }
        return "A" + new string(chars);  // Z → AA, AZ → BA, ZZ → AAA
    }

    /// <summary>
    /// Pick the next available rev code for a product given the codes
    /// already in use. Returns "A" if the list is empty. Otherwise finds
    /// the lexicographically highest code (in alpha-bump order: shorter
    /// before longer; ties broken alphabetically) and increments via
    /// <see cref="NextRev"/>.
    /// </summary>
    public static string NextAvailableRev(IEnumerable<string?> existingCodes)
    {
        string? max = null;
        foreach (var raw in existingCodes)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var c = raw.Trim().ToUpperInvariant();
            if (max is null)
            {
                max = c;
                continue;
            }
            if (CompareRev(c, max) > 0) max = c;
        }
        return max is null ? "A" : NextRev(max);
    }

    /// <summary>
    /// Compare two rev codes in the alpha-bump order: shorter is smaller
    /// (A &lt; Z &lt; AA &lt; AZ &lt; BA &lt; ZZ &lt; AAA). Equal-length
    /// codes use plain string compare.
    /// </summary>
    public static int CompareRev(string a, string b)
    {
        if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
        return string.Compare(a, b, StringComparison.Ordinal);
    }
}
