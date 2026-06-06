using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7d-1 — locks the Q3 dual-sig flag parse rules from
/// <see cref="IpqcDualSigOptionsLoader.ParseRequireDistinctQaApprover"/>.
///
/// §5.5.1 contract: default-ON discipline — only explicit OFF tokens
/// (false / 0 / off / no, case-insensitive) flip the gate. Everything
/// else (null / empty / whitespace / typos / "tru" / "yes") defaults
/// back to true so a misconfigured plant can't silently disable the
/// 4-eye review.
/// </summary>
public sealed class IpqcDualSigOptionsTests
{
    // ── Defaults ───────────────────────────────────────────────────

    [Fact]
    public void Default_options_RequireDistinctQaApprover_true()
    {
        var opts = new IpqcDualSigOptions();
        Assert.True(opts.RequireDistinctQaApprover);
        Assert.Equal("on", opts.FlagState);
    }

    [Fact]
    public void Explicit_false_init_FlagState_off()
    {
        var opts = new IpqcDualSigOptions { RequireDistinctQaApprover = false };
        Assert.False(opts.RequireDistinctQaApprover);
        Assert.Equal("off", opts.FlagState);
    }

    // ── Parse: defaults to ON for missing/unrecognised ─────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_whitespace_input_defaults_to_true(string? raw)
    {
        Assert.True(IpqcDualSigOptionsLoader.ParseRequireDistinctQaApprover(raw));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("on")]
    [InlineData("yes")]
    [InlineData("TRUE")]
    [InlineData(" On ")]
    public void Recognised_on_tokens_return_true(string raw)
    {
        Assert.True(IpqcDualSigOptionsLoader.ParseRequireDistinctQaApprover(raw));
    }

    [Theory]
    [InlineData("tru")]      // typo
    [InlineData("yse")]      // typo
    [InlineData("disable")]  // not a recognised OFF token
    [InlineData("xyz")]
    [InlineData("FALS")]     // typo — does NOT flip the gate
    public void Unrecognised_token_defaults_to_true(string raw)
    {
        // Default-ON discipline — typos can't silently disable 4-eye QC.
        Assert.True(IpqcDualSigOptionsLoader.ParseRequireDistinctQaApprover(raw));
    }

    // ── Parse: explicit OFF tokens ─────────────────────────────────

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("off")]
    [InlineData("no")]
    [InlineData("FALSE")]
    [InlineData(" Off ")]
    [InlineData("NO")]
    public void Explicit_OFF_tokens_return_false(string raw)
    {
        Assert.False(IpqcDualSigOptionsLoader.ParseRequireDistinctQaApprover(raw));
    }
}
