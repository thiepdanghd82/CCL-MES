using Microsoft.AspNetCore.Components.WebView;
#if MACCATALYST || IOS
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

#if MACCATALYST || IOS
    /// <summary>
    /// WKScriptMessageHandler bridge: JS calls
    /// <c>window.webkit.messageHandlers.cclLog.postMessage(s)</c> and we
    /// surface it via <see cref="Console.WriteLine"/>, which ends up in
    /// Console.app filtered by the CCL.MES.Hybrid process. Lets the
    /// userScript pipe DOM click/keydown events back without forcing
    /// Henry to attach Safari Web Inspector for routine smoke.
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
    /// DEEP-DEBUG hook (P10.2-LOGIN-PROBE). Enables Safari Web Inspector
    /// on the underlying WKWebView so Henry can attach via Safari →
    /// Develop → [this Mac] → CCL MES (WKWebView). Also installs a
    /// userScript that pipes window.onerror + an initial heartbeat to
    /// the Console.app log so JS load failures surface even WITHOUT
    /// Web Inspector. Remove once interactivity bug is closed.
    /// </summary>
    private void OnBlazorWebViewInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
    {
        Console.WriteLine("[BlazorWebView] Initialized — WebView object type: " + e.WebView?.GetType().FullName);

#if MACCATALYST || IOS
        try
        {
            var wkWebView = e.WebView as WebKit.WKWebView;
            if (wkWebView is null)
            {
                Console.WriteLine("[BlazorWebView] WARN: WebView is not WKWebView — got " + e.WebView?.GetType().FullName);
                return;
            }

            // Inspectable: lets Safari attach to this WKWebView. Available
            // on macOS 13.3+ / iOS 16.4+. All CCL Macs run macOS 15+ so
            // unconditional set is safe within Catalyst min OS 15.0.
            if (OperatingSystem.IsMacCatalystVersionAtLeast(16, 4) ||
                OperatingSystem.IsIOSVersionAtLeast(16, 4) ||
                OperatingSystem.IsMacOSVersionAtLeast(13, 3))
            {
                wkWebView.Inspectable = true;
                Console.WriteLine("[BlazorWebView] WKWebView.Inspectable = true (attach via Safari → Develop).");
            }
            else
            {
                Console.WriteLine("[BlazorWebView] WARN: OS too old for WKWebView.Inspectable; skipping.");
            }

            // Register the cclLog message handler that the userScript
            // posts to. Bridges JS-side events back to Console.WriteLine
            // so we see DOM click/keydown without attaching Web Inspector.
            wkWebView.Configuration.UserContentController.AddScriptMessageHandler(
                new CclLogBridge(), "cclLog");

            // Inject a userScript that surfaces JS load errors + DOM
            // click/keydown bubble-phase listeners. All output piped
            // through cclLog so it lands in Console.app.
            var probeScript = @"
                (function () {
                    function log(s) {
                        try { window.webkit.messageHandlers.cclLog.postMessage(String(s)); } catch (e) {}
                    }
                    try {
                        log('[js-probe] index.html executed.');
                        window.addEventListener('error', function (ev) {
                            log('[js-error] ' + (ev && ev.message ? ev.message : '(no msg)') + ' at ' + ((ev && ev.filename) || '?') + ':' + ((ev && ev.lineno) || '?'));
                        });
                        window.addEventListener('unhandledrejection', function (ev) {
                            log('[js-rejection] ' + (ev && ev.reason ? String(ev.reason) : '(no reason)'));
                        });
                        document.addEventListener('click', function (ev) {
                            log('[js-click] target=' + (ev.target && ev.target.tagName) + ' id=' + (ev.target && ev.target.id) + ' type=' + (ev.target && ev.target.type) + ' cx=' + ev.clientX + ' cy=' + ev.clientY);
                        }, true);
                        document.addEventListener('keydown', function (ev) {
                            log('[js-keydown] key=' + ev.key + ' target=' + (ev.target && ev.target.tagName));
                        }, true);
                        // Heartbeat after Blazor likely started — proves
                        // setTimeout JS task queue is alive.
                        setTimeout(function () { log('[js-heartbeat] T+1000ms'); }, 1000);
                        // After 2.5s, enumerate every button + report its
                        // bounding rect.
                        setTimeout(function () {
                            try {
                                var btns = document.querySelectorAll('button');
                                log('[js-buttons] count=' + btns.length);
                                btns.forEach(function (b, i) {
                                    var r = b.getBoundingClientRect();
                                    log('[js-button#' + i + '] text=' + (b.textContent || '').trim().slice(0, 24) + ' rect=' + Math.round(r.left) + ',' + Math.round(r.top) + ',' + Math.round(r.width) + ',' + Math.round(r.height) + ' type=' + b.type);
                                });
                            } catch (e) { log('[js-buttons-throw] ' + e); }
                        }, 2500);
                        // After 4s, run ONE self-test: programmatic .click()
                        // on the probe-sync button. If Henry sees
                        // [Login] probe sync clicks=1 in Console.app, the
                        // Blazor @onclick handler IS wired — proving the
                        // entire interactivity stack (blazor.webview.js +
                        // bridge + RCL component + handler delegate) is
                        // healthy. If this fires but PHYSICAL clicks do
                        // NOT, the bug is mouse-event delivery to the
                        // WKWebView, not Blazor itself.
                        setTimeout(function () {
                            try {
                                var btn = document.querySelectorAll('button')[0];
                                if (btn) {
                                    log('[js-self-test] dispatching .click() on probe-sync button.');
                                    btn.click();
                                    log('[js-self-test] .click() returned — check next line for [Login] probe sync clicks=1.');
                                }
                            } catch (e) { log('[js-self-test-throw] ' + e); }
                        }, 4000);
                    } catch (e) {
                        log('[js-probe-throw] ' + e);
                    }
                })();
            ";

            var userScript = new WebKit.WKUserScript(
                source: new Foundation.NSString(probeScript),
                injectionTime: WebKit.WKUserScriptInjectionTime.AtDocumentStart,
                isForMainFrameOnly: true);
            wkWebView.Configuration.UserContentController.AddUserScript(userScript);
            Console.WriteLine("[BlazorWebView] userScript + cclLog bridge installed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BlazorWebView] FAIL hooking diagnostics: " + ex);
        }
#endif
    }
}
