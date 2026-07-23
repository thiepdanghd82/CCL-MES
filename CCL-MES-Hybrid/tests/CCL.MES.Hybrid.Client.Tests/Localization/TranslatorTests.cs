using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Tests.Localization;

/// <summary>
/// i18n Phase-2 — Translator reads live language from ILanguageService
/// and resolves against the catalog. Fallback + format-args + live-switch.
/// </summary>
public sealed class TranslatorTests
{
    private static Translator Build(out InMemoryLanguageService lang)
    {
        lang = new InMemoryLanguageService();
        return new Translator(lang, new TranslationCatalog());
    }

    [Fact]
    public void T_returns_current_language_string()
    {
        var sut = Build(out var lang);       // default VI
        Assert.Equal("Trang chủ", sut.T("nav.home"));

        lang.Set(LanguageCode.English);
        Assert.Equal("Home", sut.T("nav.home"));  // live switch, same instance
    }

    [Fact]
    public void Current_proxies_language_service()
    {
        var sut = Build(out var lang);
        Assert.Equal(LanguageCode.Vietnamese, sut.Current);
        lang.Set(LanguageCode.English);
        Assert.Equal(LanguageCode.English, sut.Current);
    }

    [Fact]
    public void Missing_key_returns_the_key_without_throwing()
    {
        var sut = Build(out _);
        Assert.Equal("no.such.key", sut.T("no.such.key"));
    }

    [Fact]
    public void Format_args_are_applied()
    {
        // Uses a real catalog key with a placeholder-free string is fine;
        // here we prove string.Format runs when args present by using a
        // key that resolves then formatting a literal — assert no throw +
        // args honoured via the fallback path (key IS a format string).
        var sut = Build(out _);
        Assert.Equal("còn 3 ngày", sut.T("còn {0} ngày", 3));
    }
}
