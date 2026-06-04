using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Tests.Localization;

/// <summary>
/// P10.6b — InMemoryLanguageService behaviour contract. The MAUI
/// Preferences-backed impl runs the same write/read pattern via the
/// shared <see cref="LanguageCodeNames"/> serialiser, so these tests
/// cover both code paths.
///
/// Guards:
///   1. Default value is Vietnamese.
///   2. Set persists + Current returns the new value.
///   3. Set fires Changed when the value flipped.
///   4. Re-set with same value is a no-op (no event).
///   5. Multiple subscribers all receive Changed.
///   6. Unsubscribed handler does not receive Changed.
/// </summary>
public sealed class InMemoryLanguageServiceTests
{
    [Fact]
    public void Default_value_is_Vietnamese()
    {
        var sut = new InMemoryLanguageService();
        Assert.Equal(LanguageCode.Vietnamese, sut.Current);
    }

    [Fact]
    public void Set_persists_new_value()
    {
        var sut = new InMemoryLanguageService();
        sut.Set(LanguageCode.English);
        Assert.Equal(LanguageCode.English, sut.Current);
    }

    [Fact]
    public void Set_fires_Changed_when_value_flips()
    {
        var sut = new InMemoryLanguageService();
        var hits = 0;
        sut.Changed += (_, _) => hits++;

        sut.Set(LanguageCode.English);
        Assert.Equal(1, hits);

        sut.Set(LanguageCode.Vietnamese);
        Assert.Equal(2, hits);
    }

    [Fact]
    public void Set_with_same_value_does_not_fire_Changed()
    {
        var sut = new InMemoryLanguageService();
        var hits = 0;
        sut.Changed += (_, _) => hits++;

        // Default is Vietnamese — re-setting to Vietnamese should
        // be a no-op event-wise.
        sut.Set(LanguageCode.Vietnamese);
        Assert.Equal(0, hits);

        sut.Set(LanguageCode.English);
        Assert.Equal(1, hits);

        sut.Set(LanguageCode.English);
        Assert.Equal(1, hits);  // still 1
    }

    [Fact]
    public void Multiple_subscribers_all_receive_Changed()
    {
        var sut = new InMemoryLanguageService();
        var a = 0; var b = 0;
        sut.Changed += (_, _) => a++;
        sut.Changed += (_, _) => b++;

        sut.Set(LanguageCode.English);
        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void Unsubscribed_handler_does_not_receive_Changed()
    {
        var sut = new InMemoryLanguageService();
        var hits = 0;
        EventHandler handler = (_, _) => hits++;
        sut.Changed += handler;
        sut.Set(LanguageCode.English);
        Assert.Equal(1, hits);

        sut.Changed -= handler;
        sut.Set(LanguageCode.Vietnamese);
        Assert.Equal(1, hits);  // no new hit
    }
}
