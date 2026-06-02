using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 — Alpha-bump rev-code arithmetic per SpecHub
/// <c>nextRev()</c> JS algorithm. Mirrors VerifyPrB-style synthetic
/// matrix: input → expected. NextAvailableRev is the helper SpecService
/// Copy / Revise both call to avoid UNIQUE(ProductId, RevisionCode)
/// collision.
/// </summary>
public class SpecRevisionHelpersTests
{
    // ── NextRev — single-step bump ─────────────────────────────────────

    [Theory]
    [InlineData("A",   "B")]
    [InlineData("B",   "C")]
    [InlineData("Y",   "Z")]
    [InlineData("Z",   "AA")]
    [InlineData("AA",  "AB")]
    [InlineData("AZ",  "BA")]
    [InlineData("BA",  "BB")]
    [InlineData("YZ",  "ZA")]
    [InlineData("ZZ",  "AAA")]
    [InlineData("AZZ", "BAA")]
    [InlineData("ZZZ", "AAAA")]
    public void NextRev_bumps_one_step_with_rollover(string current, string expected)
    {
        Assert.Equal(expected, SpecRevisionHelpers.NextRev(current));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NextRev_treats_null_or_blank_as_starting_A(string? input)
    {
        Assert.Equal("A", SpecRevisionHelpers.NextRev(input));
    }

    [Theory]
    [InlineData("a", "B")]
    [InlineData("z", "AA")]
    [InlineData(" b ", "C")]
    public void NextRev_uppercases_and_trims_input(string input, string expected)
    {
        Assert.Equal(expected, SpecRevisionHelpers.NextRev(input));
    }

    // ── NextAvailableRev — bump highest in a list ──────────────────────

    [Fact]
    public void NextAvailableRev_returns_A_for_empty_collection()
    {
        Assert.Equal("A", SpecRevisionHelpers.NextAvailableRev(System.Array.Empty<string>()));
    }

    [Theory]
    [InlineData(new[] { "A" },           "B")]
    [InlineData(new[] { "A", "B" },      "C")]
    [InlineData(new[] { "B", "A" },      "C")]   // unordered input still picks max
    [InlineData(new[] { "Z" },           "AA")]
    [InlineData(new[] { "A", "Z" },      "AA")]
    [InlineData(new[] { "AA", "Z" },     "AB")]  // AA > Z (longer wins)
    [InlineData(new[] { "AZ" },          "BA")]
    [InlineData(new[] { "ZZ" },          "AAA")]
    public void NextAvailableRev_bumps_highest_code_in_collection(string[] existing, string expected)
    {
        Assert.Equal(expected, SpecRevisionHelpers.NextAvailableRev(existing));
    }

    [Fact]
    public void NextAvailableRev_ignores_null_and_blank_entries()
    {
        Assert.Equal("B",
            SpecRevisionHelpers.NextAvailableRev(new string?[] { null, "", "  ", "A" }));
    }

    [Fact]
    public void NextAvailableRev_normalises_case_and_whitespace()
    {
        // lowercase + padded input should be treated identically to "B"
        Assert.Equal("C",
            SpecRevisionHelpers.NextAvailableRev(new[] { "a", " B " }));
    }

    // ── CompareRev — ordering used internally by NextAvailableRev ──────

    [Theory]
    [InlineData("A",   "B",   -1)]
    [InlineData("B",   "A",    1)]
    [InlineData("A",   "A",    0)]
    [InlineData("Z",   "AA",  -1)]   // shorter < longer
    [InlineData("AA",  "Z",    1)]
    [InlineData("AA",  "AB",  -1)]
    [InlineData("ZZ",  "AAA", -1)]   // 2-char always < 3-char
    public void CompareRev_orders_shorter_before_longer_then_alpha(
        string a, string b, int expectedSign)
    {
        var actual = SpecRevisionHelpers.CompareRev(a, b);
        Assert.Equal(expectedSign, System.Math.Sign(actual));
    }
}
