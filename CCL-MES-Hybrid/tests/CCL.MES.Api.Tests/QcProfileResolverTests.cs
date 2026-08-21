using CCL.MES.Api.Services;
using CCL.MES.Application.Services;

namespace CCL.MES.Api.Tests;

/// <summary>
/// A2 thin-controller (L47) — pure unit tests for the profile/threshold
/// JSON logic extracted out of <c>WoQcReviewController</c> into
/// <see cref="QcProfileResolver"/>. These lock the extracted behaviour
/// verbatim: two override shapes + silent fallthrough on malformed JSON,
/// the sections[*].items[*].key extraction chain, and the Q4 3-level
/// resolution chain (L1 override → L2 seed → L3 "{}").
/// </summary>
public sealed class QcProfileResolverTests
{
    // ── TryExtractKindFromOverride — shape 1 (per-kind map) ──────────

    [Fact]
    public void Override_shape1_fqc_perKind_map_is_extracted()
    {
        const string overrideJson =
            """{ "fqc": { "sections": [ { "items": [ { "key": "a" } ] } ] }, "oqc": { "x": 1 } }""";

        var hit = QcProfileResolver.TryExtractKindFromOverride(overrideJson, "FQC", out var extracted);

        Assert.True(hit);
        Assert.Contains("\"key\": \"a\"", extracted);
        // Only the fqc sub-object is returned, not the whole document.
        Assert.DoesNotContain("oqc", extracted);
    }

    [Fact]
    public void Override_shape1_oqc_perKind_map_is_extracted()
    {
        const string overrideJson =
            """{ "fqc": { "a": 1 }, "oqc": { "sections": [ { "items": [ { "key": "b" } ] } ] } }""";

        var hit = QcProfileResolver.TryExtractKindFromOverride(overrideJson, "OQC", out var extracted);

        Assert.True(hit);
        Assert.Contains("\"key\": \"b\"", extracted);
    }

    // ── TryExtractKindFromOverride — shape 2 (direct profile) ────────

    [Fact]
    public void Override_shape2_direct_profile_with_matching_kind_is_extracted()
    {
        const string overrideJson =
            """{ "kind": "FQC", "sections": [ { "items": [ { "key": "c" } ] } ] }""";

        var hit = QcProfileResolver.TryExtractKindFromOverride(overrideJson, "FQC", out var extracted);

        Assert.True(hit);
        // Shape 2 returns the whole document verbatim.
        Assert.Equal(overrideJson, extracted);
    }

    [Fact]
    public void Override_shape2_kind_mismatch_falls_through()
    {
        const string overrideJson =
            """{ "kind": "FQC", "sections": [ { "items": [ { "key": "c" } ] } ] }""";

        var hit = QcProfileResolver.TryExtractKindFromOverride(overrideJson, "OQC", out var extracted);

        Assert.False(hit);
        Assert.Equal("", extracted);
    }

    // ── TryExtractKindFromOverride — empty / malformed fallthrough ───

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Override_null_or_blank_falls_through(string? overrideJson)
    {
        var hit = QcProfileResolver.TryExtractKindFromOverride(overrideJson, "FQC", out var extracted);

        Assert.False(hit);
        Assert.Equal("", extracted);
    }

    [Fact]
    public void Override_malformed_json_falls_through_silently()
    {
        const string overrideJson = """{ "fqc": { "sections": [ {  """; // truncated → JsonException

        var hit = QcProfileResolver.TryExtractKindFromOverride(overrideJson, "FQC", out var extracted);

        Assert.False(hit);
        Assert.Equal("", extracted);
    }

    [Fact]
    public void Override_non_object_root_falls_through()
    {
        var hit = QcProfileResolver.TryExtractKindFromOverride("[1,2,3]", "FQC", out var extracted);

        Assert.False(hit);
        Assert.Equal("", extracted);
    }

    // ── ResolveSnapshot — Q4 3-level chain ──────────────────────────

    [Fact]
    public void ResolveSnapshot_L1_override_hit_wins_over_seed()
    {
        const string overrideJson =
            """{ "fqc": { "sections": [ { "items": [ { "key": "override_only" } ] } ] } }""";

        var resolved = QcProfileResolver.ResolveSnapshot(overrideJson, "FQC");

        Assert.Contains("override_only", resolved);
        // Did NOT fall back to the seed default.
        Assert.NotEqual(QcProfileSeed.GetDefaultProfileJson("FQC"), resolved);
    }

    [Fact]
    public void ResolveSnapshot_L2_seed_when_override_misses()
    {
        // Override null → L1 misses → L2 seed default returned verbatim.
        var resolved = QcProfileResolver.ResolveSnapshot(null, "FQC");

        Assert.Equal(QcProfileSeed.GetDefaultProfileJson("FQC"), resolved);
        Assert.NotEqual("{}", resolved);
    }

    [Fact]
    public void ResolveSnapshot_L3_empty_when_override_misses_and_seed_null()
    {
        // Unknown kind → seed returns null → L3 "{}".
        var resolved = QcProfileResolver.ResolveSnapshot(null, "NOT_A_KIND");

        Assert.Equal("{}", resolved);
    }

    // ── ExtractProfileItemKeys ──────────────────────────────────────

    [Fact]
    public void ExtractProfileItemKeys_reads_sections_items_key_chain_in_order()
    {
        const string snapshot =
            """
            { "sections": [
                { "items": [ { "key": "k1" }, { "key": "k2" } ] },
                { "items": [ { "key": "k3" } ] }
            ] }
            """;

        var keys = QcProfileResolver.ExtractProfileItemKeys(snapshot);

        Assert.Equal(new[] { "k1", "k2", "k3" }, keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void ExtractProfileItemKeys_empty_input_yields_empty_list(string? snapshot)
    {
        Assert.Empty(QcProfileResolver.ExtractProfileItemKeys(snapshot));
    }

    [Fact]
    public void ExtractProfileItemKeys_malformed_json_yields_empty_list()
    {
        Assert.Empty(QcProfileResolver.ExtractProfileItemKeys("""{ "sections": [ { "items": """));
    }

    [Fact]
    public void ExtractProfileItemKeys_no_sections_property_yields_empty_list()
    {
        Assert.Empty(QcProfileResolver.ExtractProfileItemKeys("""{ "name": "x" }"""));
    }

    [Fact]
    public void ExtractProfileItemKeys_skips_empty_and_non_string_keys()
    {
        const string snapshot =
            """
            { "sections": [
                { "items": [ { "key": "" }, { "key": 42 }, { "nokey": "z" }, { "key": "good" } ] }
            ] }
            """;

        var keys = QcProfileResolver.ExtractProfileItemKeys(snapshot);

        Assert.Equal(new[] { "good" }, keys);
    }

    [Fact]
    public void ExtractProfileItemKeys_section_without_items_is_skipped()
    {
        const string snapshot =
            """
            { "sections": [
                { "title": "no items here" },
                { "items": [ { "key": "present" } ] }
            ] }
            """;

        Assert.Equal(new[] { "present" }, QcProfileResolver.ExtractProfileItemKeys(snapshot));
    }

    // ── ProfileKeyCount matches ExtractProfileItemKeys.Count ─────────

    [Fact]
    public void ProfileKeyCount_matches_extract_count_on_seed_default()
    {
        var fqcSeed = QcProfileSeed.GetDefaultProfileJson("FQC")!;

        Assert.Equal(
            QcProfileResolver.ExtractProfileItemKeys(fqcSeed).Count,
            QcProfileResolver.ProfileKeyCount(fqcSeed));
        // Sanity — FQC default is 12 items (matches QcProfileSeed contract).
        Assert.Equal(12, QcProfileResolver.ProfileKeyCount(fqcSeed));
    }

    [Fact]
    public void ProfileKeyCount_empty_snapshot_is_zero()
    {
        Assert.Equal(0, QcProfileResolver.ProfileKeyCount("{}"));
        Assert.Equal(0, QcProfileResolver.ProfileKeyCount(null));
    }
}
