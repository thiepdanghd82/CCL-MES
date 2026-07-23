using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Tests.Localization;

/// <summary>
/// i18n Phase-2 — catalog contract. The parity test is the guard that
/// keeps every migration batch honest: a key with only one language slot
/// (or an empty string) fails CI, so no half-translated key ever ships.
/// </summary>
public sealed class TranslationCatalogTests
{
    private static readonly LanguageCode[] AllLangs =
        { LanguageCode.Vietnamese, LanguageCode.English };

    [Fact]
    public void Every_key_has_both_languages_non_empty()
    {
        var catalog = new TranslationCatalog();
        Assert.NotEmpty(catalog.All);

        var offenders = new List<string>();
        foreach (var (key, perLang) in catalog.All)
        {
            foreach (var lang in AllLangs)
            {
                if (!perLang.TryGetValue(lang, out var s) || string.IsNullOrWhiteSpace(s))
                    offenders.Add($"{key} [{lang}]");
            }
        }

        Assert.True(offenders.Count == 0,
            "Keys missing a language slot (parity — every key needs VI + EN):\n" +
            string.Join("\n", offenders));
    }

    [Fact]
    public void Lookup_returns_the_language_specific_string()
    {
        var catalog = new TranslationCatalog();
        Assert.Equal("Trang chủ", catalog.Lookup("nav.home", LanguageCode.Vietnamese));
        Assert.Equal("Home", catalog.Lookup("nav.home", LanguageCode.English));
    }

    [Fact]
    public void Lookup_missing_key_returns_null()
    {
        var catalog = new TranslationCatalog();
        Assert.Null(catalog.Lookup("does.not.exist", LanguageCode.Vietnamese));
    }

    [Fact]
    public void Batch1_surfaces_are_covered()
    {
        var catalog = new TranslationCatalog();
        // Spot-check one key from each batch-1 surface.
        foreach (var key in new[] { "nav.home", "topbar.user", "common.logout", "appearance.title" })
            Assert.NotNull(catalog.Lookup(key, LanguageCode.English));
    }
}
