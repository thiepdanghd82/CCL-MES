using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Localization;

/// <summary>
/// The key → per-language string table backing <see cref="ITranslator"/>.
/// Split from the translator so tests can assert parity/coverage against
/// the raw table without going through language-state plumbing.
/// </summary>
public interface ITranslationCatalog
{
    /// <summary>Resolve one key in one language, or <c>null</c> when the
    /// key (or that language slot) is absent.</summary>
    string? Lookup(string key, LanguageCode lang);

    /// <summary>Every registered key with its full per-language map.
    /// Used by the parity test to enforce EN+VI on every entry.</summary>
    IReadOnlyDictionary<string, IReadOnlyDictionary<LanguageCode, string>> All { get; }
}
