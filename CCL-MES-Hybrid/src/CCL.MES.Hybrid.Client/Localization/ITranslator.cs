using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Localization;

/// <summary>
/// i18n Phase-2 (option B — dict-based). Live UI string lookup keyed off
/// the operator's <see cref="ILanguageService.Current"/> choice. This is
/// the "translation service" the P10.6b <see cref="ILanguageService"/>
/// comment promised would "subscribe to Changed" — now real.
///
/// Design (mirror SharedResource.resx namespacing so a future lift to
/// <c>IStringLocalizer</c>/resx — audit §4 option A — reuses the keys):
///   key = "namespace.leaf"  (nav.* / topbar.* / common.* / settings.* …)
///
/// Lookups NEVER throw. A missing key returns the key itself (visible in
/// dev, harmless in prod) so a half-migrated screen degrades gracefully
/// instead of blanking. Components re-render on language change by
/// subscribing to <see cref="ILanguageService.Changed"/> — see
/// <c>LocalizedComponentBase</c>.
/// </summary>
public interface ITranslator
{
    /// <summary>The operator's active language (proxied from
    /// <see cref="ILanguageService.Current"/>).</summary>
    LanguageCode Current { get; }

    /// <summary>Resolve <paramref name="key"/> in the current language.
    /// When <paramref name="args"/> are supplied the resolved string is
    /// run through <see cref="string.Format(string, object?[])"/> (for
    /// composed strings like <c>"còn {0} ngày"</c>). Missing key →
    /// returns <paramref name="key"/> unchanged (never throws).</summary>
    string T(string key, params object[] args);
}
