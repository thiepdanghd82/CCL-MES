using CCL.MES.Shared.Localization;

namespace CCL.MES.Hybrid.Client.Localization;

/// <summary>
/// P10.6b — operator-facing UI language preference store.
///
/// Mirrors the SpecHub §11 Appearance picker (EN/VN) ported to MAUI
/// Preferences. Pure persistence layer this PR: actual string
/// swapping requires the resx infrastructure deferred until P10.6+
/// per Q12. The picker IS functional — operator's choice is recorded
/// + survives app restart — but only the Vietnamese strings exist
/// today; the page surfaces a disclaimer banner so operators don't
/// expect the swap to take effect mid-session.
///
/// Theme switcher is OUT OF SCOPE for P10.6b. It ships in P10.6g
/// (IMP-2 conditional). Conflating the two would force a re-test
/// of every page when only the language picker landed; keeping them
/// separate matches the §5.2 ship order.
///
/// Mirror of <c>IRecentScansService</c> + <c>IGridPreferenceStore</c>
/// pattern. MAUI host registers <c>MauiLanguageService</c> via DI
/// Replace; tests + non-MAUI hosts keep the in-memory default.
/// </summary>
public interface ILanguageService
{
    /// <summary>The operator's persisted choice. Defaults to
    /// <see cref="LanguageCode.Vietnamese"/> on first use (matches
    /// the only language whose strings actually exist today).</summary>
    LanguageCode Current { get; }

    /// <summary>Persist a new choice. Triggers <see cref="Changed"/>
    /// only when the value actually flipped — idempotent re-sets
    /// skip the event so subscribers don't re-render needlessly.</summary>
    void Set(LanguageCode code);

    /// <summary>Fires after <see cref="Set"/> mutates the store.
    /// Future resx-backed translation service will subscribe to
    /// re-render every active page.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// Process-scoped default. Used by tests, by non-MAUI hosts, and as
/// the DI fallback if the MAUI host's Replace step is skipped (e.g.
/// dev harness, Razor library compile-time analysers).
/// </summary>
public sealed class InMemoryLanguageService : ILanguageService
{
    private readonly object _lock = new();
    private LanguageCode _current = LanguageCode.Vietnamese;

    public event EventHandler? Changed;

    public LanguageCode Current
    {
        get { lock (_lock) { return _current; } }
    }

    public void Set(LanguageCode code)
    {
        bool mutated;
        lock (_lock)
        {
            mutated = _current != code;
            _current = code;
        }
        if (mutated) Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Static helpers shared between the picker UI + the Preferences
/// layer so the parse + serialise contract has a single definition.
/// </summary>
public static class LanguageCodeNames
{
    /// <summary>Vietnamese display label (used inside the picker
    /// UI regardless of operator choice — the label IS the
    /// language's own name in its own script).</summary>
    public static string LabelVi(LanguageCode code) => code switch
    {
        LanguageCode.Vietnamese => "Tiếng Việt",
        LanguageCode.English    => "English",
        _                       => code.ToString(),
    };

    /// <summary>English display label — currently identical to
    /// <see cref="LabelVi"/> by accident of the language inventory
    /// (both labels are endonyms). Kept separate so a future
    /// resx swap can route through here.</summary>
    public static string LabelEn(LanguageCode code) => LabelVi(code);

    /// <summary>Serialise to the enum NAME — robust against
    /// re-ordering. A future operator who flips to English then
    /// upgrades the app to a version that adds a third language
    /// keeps their preference.</summary>
    public static string ToPreferenceString(LanguageCode code) => code.ToString();

    /// <summary>Parse a persisted preference string back to a
    /// <see cref="LanguageCode"/>. Falls back to
    /// <see cref="LanguageCode.Vietnamese"/> on anything
    /// unrecognised — covers the "store was edited by hand" +
    /// "future code value rolled back" cases without crashing.
    ///
    /// Numeric input ("0", "1") is REJECTED on purpose: the whole
    /// reason we persist the enum name is so a future PR can
    /// re-order values without silently flipping a stored
    /// preference. Accepting the int form would defeat that.</summary>
    public static LanguageCode FromPreferenceString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return LanguageCode.Vietnamese;
        var trimmed = raw.Trim();
        if (trimmed.Length > 0 && trimmed.All(char.IsDigit))
            return LanguageCode.Vietnamese;
        return Enum.TryParse<LanguageCode>(trimmed, ignoreCase: true, out var parsed)
            ? parsed
            : LanguageCode.Vietnamese;
    }
}
