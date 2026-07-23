using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Localization;

/// <summary>
/// Default <see cref="ITranslator"/> — reads the live language from
/// <see cref="ILanguageService"/> on every lookup (so a mid-session
/// switch takes effect the instant components re-render) and resolves
/// against the singleton <see cref="ITranslationCatalog"/>.
/// </summary>
public sealed class Translator : ITranslator
{
    private readonly ILanguageService _language;
    private readonly ITranslationCatalog _catalog;

    public Translator(ILanguageService language, ITranslationCatalog catalog)
    {
        _language = language;
        _catalog = catalog;
    }

    public LanguageCode Current => _language.Current;

    public string T(string key, params object[] args)
    {
        var resolved = _catalog.Lookup(key, _language.Current) ?? key;
        return args is { Length: > 0 } ? string.Format(resolved, args) : resolved;
    }
}
