using CCL.MES.Hybrid.Client.WorkOrders;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P10.7a-1.3 — locks the WO-code normaliser the manual-entry input
/// + the scan handler both call. The whole point of the helper is
/// that the scan path + the manual path can't drift on character
/// handling; these tests are what guarantees that.
/// </summary>
public sealed class WoCodeNormalizerTests
{
    // ── Normalize — basic shape ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Normalize_returns_null_for_empty_or_whitespace(string? raw)
    {
        Assert.Null(WoCodeNormalizer.Normalize(raw));
    }

    [Fact]
    public void Normalize_trims_surrounding_whitespace()
    {
        Assert.Equal("WO-26-3683", WoCodeNormalizer.Normalize("  WO-26-3683  "));
        Assert.Equal("WO-26-3683", WoCodeNormalizer.Normalize("\tWO-26-3683\n"));
    }

    [Fact]
    public void Normalize_preserves_internal_dashes_and_case()
    {
        // WO codes have meaningful case suffixes for some customers
        // (e.g. lowercase 'a' on a re-issued WO). We do NOT uppercase
        // because the server lookup is case-sensitive.
        Assert.Equal("WO-26-3683a", WoCodeNormalizer.Normalize("WO-26-3683a"));
    }

    [Fact]
    public void Normalize_strips_zero_width_space()
    {
        // Catalyst soft keyboard occasionally injects U+200B between
        // segments when the operator pastes from clipboard.
        var withZwsp = "WO-26-​3683";
        Assert.Equal("WO-26-3683", WoCodeNormalizer.Normalize(withZwsp));
    }

    [Fact]
    public void Normalize_strips_other_control_chars()
    {
        // U+0007 BEL, U+200C ZWNJ, U+200D ZWJ — none should reach the
        // server's lookup table.
        var dirty = "WO-26-3683‌‍";
        Assert.Equal("WO-26-3683", WoCodeNormalizer.Normalize(dirty));
    }

    // ── NormalizeForManualEntry — adds min-length gate ───────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("W")]    // 1 char
    [InlineData("WO")]   // 2 chars
    public void ManualEntry_rejects_too_short(string? raw)
    {
        Assert.Null(WoCodeNormalizer.NormalizeForManualEntry(raw));
    }

    [Theory]
    [InlineData("WO1", "WO1")]            // exactly 3
    [InlineData("WO-26-3683", "WO-26-3683")]
    [InlineData("  WO-26-3683  ", "WO-26-3683")]
    public void ManualEntry_accepts_three_chars_or_more_and_normalises(string raw, string expected)
    {
        Assert.Equal(expected, WoCodeNormalizer.NormalizeForManualEntry(raw));
    }
}
