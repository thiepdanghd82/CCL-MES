using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Shared.RecentScans;

namespace CCL.MES.Hybrid.Client.Tests.RecentScans;

/// <summary>
/// P10.6f — the shared (de)serialiser is the contract between
/// <see cref="InMemoryRecentScansService"/> and the MAUI-host
/// Preferences-backed impl. If these tests break, the persisted
/// snapshot can't round-trip after an app restart.
/// </summary>
public sealed class RecentScansSerializerTests
{
    [Fact]
    public void Round_trip_preserves_all_fields()
    {
        var src = new RecentScanEntry
        {
            Code = "WO-2026-00123",
            Format = "Code128",
            Context = "wo-accept",
            ScannedAt = new DateTime(2026, 6, 4, 10, 30, 45, DateTimeKind.Utc),
            ResolvedDisplay = "WO-2026-00123",
            ResolvedSubtitle = "PRD-FOO · ACME",
            Resolved = true,
        };
        var raw = RecentScansSerializer.Serialize(new[] { src });
        var back = RecentScansSerializer.Deserialize(raw);

        Assert.Single(back);
        Assert.Equal(src.Code, back[0].Code);
        Assert.Equal(src.Format, back[0].Format);
        Assert.Equal(src.Context, back[0].Context);
        Assert.Equal(src.ScannedAt, back[0].ScannedAt);
        Assert.Equal(src.ResolvedDisplay, back[0].ResolvedDisplay);
        Assert.Equal(src.ResolvedSubtitle, back[0].ResolvedSubtitle);
        Assert.Equal(src.Resolved, back[0].Resolved);
    }

    [Fact]
    public void Deserialize_empty_string_returns_empty_list()
    {
        Assert.Empty(RecentScansSerializer.Deserialize(string.Empty));
    }

    [Fact]
    public void Deserialize_null_returns_empty_list()
    {
        Assert.Empty(RecentScansSerializer.Deserialize(null));
    }

    [Fact]
    public void Deserialize_corrupt_json_returns_empty_list_not_throw()
    {
        // Wipe-on-read is the chosen recovery — operator just loses
        // local history rather than seeing a crashing widget.
        var corrupt = "this is not json at all {";
        var result = RecentScansSerializer.Deserialize(corrupt);
        Assert.Empty(result);
    }

    [Fact]
    public void Serialize_uses_camelCase_property_names()
    {
        var src = new RecentScanEntry { Code = "X", Context = "y" };
        var raw = RecentScansSerializer.Serialize(new[] { src });
        // camelCase contract — JS-side dev tools + manual Preferences
        // inspection both expect "code" / "scannedAt", not "Code" /
        // "ScannedAt".
        Assert.Contains("\"code\":", raw);
        Assert.DoesNotContain("\"Code\":", raw);
    }
}
