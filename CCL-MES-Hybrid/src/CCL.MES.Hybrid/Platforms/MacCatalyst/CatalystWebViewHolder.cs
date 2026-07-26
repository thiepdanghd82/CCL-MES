#if MACCATALYST || IOS
using WebKit;

namespace CCL.MES.Hybrid.Platforms.MacCatalyst;

/// <summary>
/// Holds the live <see cref="WKWebView"/> reference captured from
/// <c>MainPage.OnBlazorWebViewInitialized</c> so platform services (native
/// print) can reach the actual WebView that renders the Blazor DOM.
///
/// A plain static is used deliberately: the BlazorWebView is created by
/// MAUI (not via DI — see <c>App.MainPage = new MainPage()</c>), so there
/// is no injected singleton to write into from the initialised hook. The
/// app hosts exactly ONE BlazorWebView, so a single static ref is correct
/// and there is no lifetime ambiguity. Set once on init; read on demand.
/// </summary>
public static class CatalystWebViewHolder
{
    public static WKWebView? WebView { get; set; }
}
#endif
