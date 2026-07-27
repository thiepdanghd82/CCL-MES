using CCL.MES.Hybrid.Client.Localization;
using Microsoft.AspNetCore.Components;

namespace CCL.MES.Hybrid.Razor.Localization;

/// <summary>
/// Base for components that render translated strings. Injects
/// <see cref="ITranslator"/>, exposes <see cref="T"/>, and re-renders the
/// component whenever the operator flips language (subscribes to
/// <see cref="ILanguageService.Changed"/>). This is what makes the picker
/// take effect LIVE — no app restart.
///
/// Usage in a .razor file:  <c>@inherits LocalizedComponentBase</c> then
/// <c>@T("nav.home")</c>. Components that need their own
/// <c>OnInitialized</c>/<c>Dispose</c> must chain to <c>base</c>.
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    [Inject] protected ITranslator Loc { get; set; } = default!;
    [Inject] protected ILanguageService LanguageService { get; set; } = default!;

    /// <summary>Resolve a translation key in the active language.</summary>
    protected string T(string key, params object[] args) => Loc.T(key, args);

    protected override void OnInitialized() => LanguageService.Changed += OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => LanguageService.Changed -= OnLanguageChanged;
}
