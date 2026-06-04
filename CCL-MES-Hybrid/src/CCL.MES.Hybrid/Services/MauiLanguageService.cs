using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Shared.Localization;
using Microsoft.Maui.Storage;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// MAUI host impl of <see cref="ILanguageService"/>. Persists the
/// operator's choice as the enum NAME under MAUI Preferences key
/// <c>cclmes.hybrid.language.v1</c>. Mirror of
/// <see cref="MauiRecentScansService"/> + <see cref="MauiGridPreferenceStore"/>:
/// <c>Preferences.Default</c> is documented thread-safe so a single
/// in-process lock guards the read-modify-write race the WebView
/// dispatcher can otherwise trip.
///
/// Serialise/parse contract lives in <see cref="LanguageCodeNames"/>
/// so the in-memory + Preferences-backed impls cannot drift on the
/// persisted shape.
/// </summary>
public sealed class MauiLanguageService : ILanguageService
{
    private const string PreferenceKey = "cclmes.hybrid.language.v1";
    private readonly object _lock = new();

    public event EventHandler? Changed;

    public LanguageCode Current
    {
        get
        {
            lock (_lock)
            {
                var raw = Preferences.Default.Get(PreferenceKey, string.Empty);
                return LanguageCodeNames.FromPreferenceString(raw);
            }
        }
    }

    public void Set(LanguageCode code)
    {
        bool mutated;
        lock (_lock)
        {
            var existingRaw = Preferences.Default.Get(PreferenceKey, string.Empty);
            var existing = LanguageCodeNames.FromPreferenceString(existingRaw);
            mutated = existing != code;
            if (mutated)
                Preferences.Default.Set(PreferenceKey, LanguageCodeNames.ToPreferenceString(code));
        }
        if (mutated) Changed?.Invoke(this, EventArgs.Empty);
    }
}
