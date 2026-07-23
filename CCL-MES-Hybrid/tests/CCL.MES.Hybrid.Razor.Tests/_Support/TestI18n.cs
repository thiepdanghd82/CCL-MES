using CCL.MES.Hybrid.Client.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// i18n Phase-2 test helper. Any bUnit test that renders a
/// LocalizedComponentBase-derived component (or one that @inject-s
/// ITranslator) needs the translator wired. One <c>Services.AddI18n()</c>
/// call registers the real InMemoryLanguageService + TranslationCatalog +
/// Translator so translated strings render (default Vietnamese) and a
/// language flip re-renders live.
/// </summary>
public static class TestI18n
{
    public static IServiceCollection AddI18n(this IServiceCollection services)
    {
        services.AddSingleton<ILanguageService, InMemoryLanguageService>();
        services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        services.AddSingleton<ITranslator, Translator>();
        return services;
    }
}
