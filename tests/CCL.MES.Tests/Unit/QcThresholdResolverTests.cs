using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7e-1 Q4 — exhaustive coverage of the 3-level threshold
/// resolution chain. Mirrors the SpecHub MES_QUALITY_REDESIGN_PLAN.md
/// per-product override design.
///
/// Resolution chain (highest wins):
///   1. Product.QcProfileOverride
///   2. WoQcCheck.ProfileSnapshotJson
///   3. Hardcoded item-definition default
///
/// Tests cover EACH level winning + each level being skipped on null,
/// missing key, missing threshold field, wrong field type, malformed
/// JSON.
/// </summary>
public sealed class QcThresholdResolverTests
{
    // ── Level 1 — Product override wins ────────────────────────────

    [Fact]
    public void Product_override_wins_over_profile_snapshot_and_default()
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: """{"color_dE": {"threshold": 1.0}}""",
            profileSnapshotJson: """{"color_dE": {"threshold": 2.0}}""",
            hardcodedDefault: 3.0);
        Assert.Equal(1.0, threshold);
    }

    // ── Level 2 — Profile snapshot wins when override is null ──────

    [Fact]
    public void Profile_snapshot_wins_when_override_is_null()
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: null,
            profileSnapshotJson: """{"color_dE": {"threshold": 2.0}}""",
            hardcodedDefault: 3.0);
        Assert.Equal(2.0, threshold);
    }

    [Fact]
    public void Profile_snapshot_wins_when_override_lacks_item_key()
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: """{"different_item": {"threshold": 1.0}}""",
            profileSnapshotJson: """{"color_dE": {"threshold": 2.0}}""",
            hardcodedDefault: 3.0);
        Assert.Equal(2.0, threshold);
    }

    [Fact]
    public void Profile_snapshot_wins_when_override_lacks_threshold_field()
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: """{"color_dE": {"enabled": true}}""",
            profileSnapshotJson: """{"color_dE": {"threshold": 2.0}}""",
            hardcodedDefault: 3.0);
        Assert.Equal(2.0, threshold);
    }

    // ── Level 3 — Hardcoded default wins when both upper levels miss ─

    [Fact]
    public void Hardcoded_default_wins_when_both_override_and_snapshot_are_null()
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: null,
            profileSnapshotJson: null,
            hardcodedDefault: 3.0);
        Assert.Equal(3.0, threshold);
    }

    [Fact]
    public void Hardcoded_default_wins_when_neither_level_carries_item_key()
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: """{"other_item": {"threshold": 1.0}}""",
            profileSnapshotJson: """{"yet_another": {"threshold": 2.0}}""",
            hardcodedDefault: 3.0);
        Assert.Equal(3.0, threshold);
    }

    // ── Robustness — malformed JSON falls through, no throw ────────

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]     // root is array, not object
    [InlineData("null")]           // JSON null
    [InlineData("\"a string\"")]   // string root
    [InlineData("{\"missing closing brace")]
    public void Malformed_override_json_falls_through_to_snapshot(string overrideJson)
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: overrideJson,
            profileSnapshotJson: """{"color_dE": {"threshold": 2.0}}""",
            hardcodedDefault: 3.0);
        Assert.Equal(2.0, threshold);
    }

    [Fact]
    public void Malformed_both_levels_falls_through_to_default()
    {
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: "{garbage}",
            profileSnapshotJson: "more garbage",
            hardcodedDefault: 3.0);
        Assert.Equal(3.0, threshold);
    }

    [Fact]
    public void Wrong_type_at_threshold_field_falls_through()
    {
        // threshold is a string, not a number → invalid → fall through.
        var threshold = QcThresholdResolver.Resolve(
            itemKey: "color_dE",
            productOverrideJson: """{"color_dE": {"threshold": "abc"}}""",
            profileSnapshotJson: """{"color_dE": {"threshold": 2.0}}""",
            hardcodedDefault: 3.0);
        Assert.Equal(2.0, threshold);
    }

    // ── IsEnabled — boolean resolution chain ───────────────────────

    [Fact]
    public void IsEnabled_defaults_to_true_when_no_level_overrides()
    {
        Assert.True(QcThresholdResolver.IsEnabled(
            itemKey: "color_dE",
            productOverrideJson: null,
            profileSnapshotJson: null));
    }

    [Fact]
    public void IsEnabled_product_override_can_disable()
    {
        Assert.False(QcThresholdResolver.IsEnabled(
            itemKey: "color_dE",
            productOverrideJson: """{"color_dE": {"enabled": false}}""",
            profileSnapshotJson: """{"color_dE": {"enabled": true}}"""));
    }

    [Fact]
    public void IsEnabled_profile_snapshot_can_disable_when_override_silent()
    {
        Assert.False(QcThresholdResolver.IsEnabled(
            itemKey: "color_dE",
            productOverrideJson: null,
            profileSnapshotJson: """{"color_dE": {"enabled": false}}"""));
    }

    [Fact]
    public void IsEnabled_product_override_can_re_enable_a_profile_disabled_item()
    {
        // Customer X wants the color check even though the global Silk
        // profile turned it off (e.g. operator-error backlash). Q4
        // override re-enables it.
        Assert.True(QcThresholdResolver.IsEnabled(
            itemKey: "color_dE",
            productOverrideJson: """{"color_dE": {"enabled": true}}""",
            profileSnapshotJson: """{"color_dE": {"enabled": false}}"""));
    }
}
