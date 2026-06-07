using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7e-1 Q5 + Lesson L20 — locks the parse + flag-state rules from
/// <see cref="WoQcSigPolicyOptionsLoader.ParseFlag"/> and the
/// <see cref="WoQcSigPolicyOptions"/> defaults.
///
/// Default-ON discipline (mirrors the 7d Q3 dual-sig pattern, codified
/// as L20): only explicit OFF tokens (false / 0 / off / no,
/// case-insensitive, trimmed) flip a flag. Everything else (null /
/// empty / whitespace / typos / uppercase variants) defaults back to
/// true so a misconfigured plant cannot silently disable any of the
/// 3 OQC distinct-user invariants.
/// </summary>
public sealed class WoQcSigPolicyOptionsTests
{
    // ── Defaults ───────────────────────────────────────────────────

    [Fact]
    public void Default_options_all_3_flags_true()
    {
        var opts = new WoQcSigPolicyOptions();
        Assert.True(opts.OqcRequireDistinctReviewer);
        Assert.True(opts.OqcRequireDistinctApprover);
        Assert.True(opts.OqcRequireApproverDistinctFromInspector);
        Assert.True(opts.AllFlagsOn);
        Assert.Equal("R=on;A=on;AI=on", opts.FlagState);
    }

    [Fact]
    public void All_3_flags_off_FlagState_reports_off_triple()
    {
        var opts = new WoQcSigPolicyOptions
        {
            OqcRequireDistinctReviewer = false,
            OqcRequireDistinctApprover = false,
            OqcRequireApproverDistinctFromInspector = false,
        };
        Assert.False(opts.AllFlagsOn);
        Assert.Equal("R=off;A=off;AI=off", opts.FlagState);
    }

    [Fact]
    public void Partial_off_FlagState_mixes_on_and_off_per_flag()
    {
        var opts = new WoQcSigPolicyOptions
        {
            OqcRequireDistinctReviewer = true,
            OqcRequireDistinctApprover = false,
            OqcRequireApproverDistinctFromInspector = true,
        };
        Assert.False(opts.AllFlagsOn);
        Assert.Equal("R=on;A=off;AI=on", opts.FlagState);
    }

    // ── Parse table (12 cases mirror IpqcDualSigOptionsTests) ──────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\t\n")]
    [InlineData("true")]
    [InlineData("TRUE")]
    [InlineData("yes")]
    [InlineData("on")]
    [InlineData("trie")]      // typo of "true" — must default to ON
    [InlineData("Falsey")]    // not exact "false" — default to ON
    [InlineData("0n")]        // typo of "on" — default to ON
    [InlineData("1")]         // not an OFF token — ON
    public void ParseFlag_default_ON_for_null_empty_typo_or_unrecognised(string? raw)
    {
        Assert.True(WoQcSigPolicyOptionsLoader.ParseFlag(raw));
    }

    [Theory]
    [InlineData("false")]
    [InlineData("FALSE")]
    [InlineData("False")]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("OFF")]
    [InlineData("no")]
    [InlineData("NO")]
    [InlineData(" false ")]   // whitespace trimmed before match
    [InlineData("\toff\n")]
    public void ParseFlag_returns_false_only_for_explicit_OFF_tokens(string raw)
    {
        Assert.False(WoQcSigPolicyOptionsLoader.ParseFlag(raw));
    }
}
