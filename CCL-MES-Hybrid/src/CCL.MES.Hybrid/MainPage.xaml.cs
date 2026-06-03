using Microsoft.AspNetCore.Components.WebView;
#if DEBUG && (MACCATALYST || IOS)
using Foundation;
using WebKit;
#endif

namespace CCL.MES.Hybrid;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

#if DEBUG && (MACCATALYST || IOS)
    /// <summary>
    /// Debug-only bridge: receives JS console messages and forwards them
    /// to <see cref="Console.WriteLine"/> so Catalyst Console.app shows
    /// browser-level logs without the developer needing to attach Safari
    /// Web Inspector. Pure observation — does NOT modify the DOM.
    /// </summary>
    private sealed class CclLogBridge : NSObject, IWKScriptMessageHandler
    {
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            Console.WriteLine("[js] " + message.Body);
        }
    }
#endif

    /// <summary>
    /// BlazorWebView initialised hook. In DEBUG builds we attach two
    /// Catalyst-only diagnostics:
    ///   1. WKWebView.Inspectable = true so Safari → Develop can attach
    ///      a Web Inspector to the WebView.
    ///   2. A userScript that pipes window.onerror + DOM keydown logs
    ///      back to Console.app via the cclLog message handler. This
    ///      makes JS errors visible without external tooling.
    /// In RELEASE builds the hook is a no-op — no inspectability, no
    /// JS observers, no userScript. The Tab/Enter Catalyst keyboard
    /// workaround (dotnet/maui#13934) is a separate, ALWAYS-ON
    /// component — see <c>CCL.MES.Hybrid.Razor/Shared/MacCatalystKeyboardFix.razor</c>.
    /// </summary>
    private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
#if DEBUG && (MACCATALYST || IOS)
        try
        {
            var wkWebView = e.WebView as WebKit.WKWebView;
            if (wkWebView is null) return;

            if (OperatingSystem.IsMacCatalystVersionAtLeast(16, 4) ||
                OperatingSystem.IsIOSVersionAtLeast(16, 4) ||
                OperatingSystem.IsMacOSVersionAtLeast(13, 3))
            {
                wkWebView.Inspectable = true;
                Console.WriteLine("[BlazorWebView/DEBUG] WKWebView.Inspectable = true (attach via Safari → Develop).");
            }

            wkWebView.Configuration.UserContentController.AddScriptMessageHandler(
                new CclLogBridge(), "cclLog");

            var probeScript = @"
                (function () {
                    function log(s) {
                        try { window.webkit.messageHandlers.cclLog.postMessage(String(s)); } catch (e) {}
                    }
                    window.addEventListener('error', function (ev) {
                        log('[js-error] ' + (ev && ev.message ? ev.message : '(no msg)') +
                            ' at ' + ((ev && ev.filename) || '?') + ':' + ((ev && ev.lineno) || '?'));
                    });
                    window.addEventListener('unhandledrejection', function (ev) {
                        log('[js-rejection] ' + (ev && ev.reason ? String(ev.reason) : '(no reason)'));
                    });
                })();
            ";
            var userScript = new WebKit.WKUserScript(
                source: new Foundation.NSString(probeScript),
                injectionTime: WebKit.WKUserScriptInjectionTime.AtDocumentStart,
                isForMainFrameOnly: true);
            wkWebView.Configuration.UserContentController.AddUserScript(userScript);
            Console.WriteLine("[BlazorWebView/DEBUG] cclLog bridge + window.onerror observer installed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BlazorWebView/DEBUG] FAIL hooking diagnostics: " + ex);
        }
#endif
    }
}
