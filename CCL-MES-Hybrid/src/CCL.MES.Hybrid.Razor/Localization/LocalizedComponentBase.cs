using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Client.Status;
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

    /// <summary>
    /// Nhãn NGƯỜI ĐỌC của một token phase (<c>MesPhase</c> hoặc <c>LegPhase</c>)
    /// theo ngôn ngữ đang bật — <c>"IPQC_WAIT"</c> → <c>"Chờ IPQC"</c>.
    ///
    /// <para><b>Vì sao nằm ở base chứ không ở từng trang.</b> Từ ngữ 15 phase đã
    /// chốt một lần trong <c>TranslationCatalog.Legs</c> (Henry duyệt), và
    /// <see cref="PhaseVisual.LabelKey"/> đã biết đường từ token sang key.
    /// Nhưng chỉ <c>StatusPill</c> đi qua đường đó; 6 surface khác in thẳng
    /// <c>@_view.MesPhase</c> ra markup nên người đứng máy vẫn đọc token thô.
    /// Mỗi trang tự nối <c>T("legs.phase." + p.ToLower())</c> là mời lại đúng
    /// cái bệnh cũ: 4 bản sao, lệch nhau lúc nào không biết.</para>
    ///
    /// <para>Token lạ (contract mở rộng mà quên thêm nhãn) trả về CHÍNH token —
    /// hiện <c>IPQC_WAIT</c> vẫn hơn hiện ô trống. Nhưng đó là đường lùi cho
    /// runtime, không phải chỗ dựa: <c>gate-phase-label.sh</c> bắt tĩnh trước.
    /// Rỗng/null trả <c>"—"</c> để ô không sập chiều cao.</para>
    /// </summary>
    protected string PhaseText(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase)) return "—";
        var key = PhaseVisual.LabelKey(phase);
        return key is null ? phase! : T(key);
    }

    protected override void OnInitialized() => LanguageService.Changed += OnLanguageChanged;

    private void OnLanguageChanged(object? sender, EventArgs e) => InvokeAsync(StateHasChanged);

    public virtual void Dispose() => LanguageService.Changed -= OnLanguageChanged;
}
