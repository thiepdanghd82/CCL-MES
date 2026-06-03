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
                        // Fine-grained event tracing so we can distinguish
                        // (a) event never reaches WKWebView DOM
                        // (b) reaches DOM but Blazor delegate misses it
                        ['pointerdown', 'pointerup', 'mousedown', 'mouseup', 'click'].forEach(function (etype) {
                            document.addEventListener(etype, function (ev) {
                                log('[js-' + etype + '] target=' + (ev.target && ev.target.tagName) + ' id=' + (ev.target && ev.target.id) + ' type=' + (ev.target && ev.target.type) + ' cx=' + ev.clientX + ' cy=' + ev.clientY);
                            }, true);
                        });
                        document.addEventListener('keydown', function (ev) {
                            log('[js-keydown] key=' + ev.key + ' code=' + ev.code + ' target=' + (ev.target && ev.target.tagName) + ' shift=' + ev.shiftKey);
                        }, true);
                        document.addEventListener('input', function (ev) {
                            log('[js-input] target=' + (ev.target && ev.target.tagName) + ' id=' + (ev.target && ev.target.id) + ' val.len=' + (ev.target && ev.target.value || '').length);
                        }, true);
                        // ────────────────────────────────────────────────
                        // KNOWN ISSUE WORKAROUND — dotnet/maui#13934.
                        // On Mac Catalyst, when an <input> element has
                        // focus, the WKWebView traps Tab/Enter inside that
                        // input and never moves focus to the next element.
                        // Native Mac apps, Windows BlazorWebView, and iOS
                        // are all unaffected — it's a Catalyst-specific
                        // WKWebView bug Apple has not fixed since 2023
                        // (Apple Feedback FB12076485, still 'Open').
                        // Community workaround (@Angineer48 in the issue
                        // thread): intercept Tab at document level,
                        // preventDefault, and manually move focus via
                        // .focus() on the next focusable element. We also
                        // promote Enter inside form inputs to submit the
                        // form because the native WKWebView form-submit
                        // chain has the same trap.
                        // ────────────────────────────────────────────────
                        document.addEventListener('keydown', function (ev) {
                            if (ev.code === 'Tab') {
                                var sel = 'a:not([disabled]):not([tabindex=""-1""]), button:not([disabled]):not([tabindex=""-1""]), input:not([disabled]):not([tabindex=""-1""]), select:not([disabled]):not([tabindex=""-1""]), textarea:not([disabled]):not([tabindex=""-1""]), [tabindex]:not([disabled]):not([tabindex=""-1""])';
                                var focusables = Array.prototype.filter.call(document.querySelectorAll(sel), function (el) {
                                    return el.offsetWidth > 0 || el.offsetHeight > 0 || el === document.activeElement;
                                });
                                var idx = focusables.indexOf(document.activeElement);
                                if (idx > -1) {
                                    ev.preventDefault();
                                    var nextIdx = idx + (ev.shiftKey ? -1 : 1);
                                    var next = focusables[nextIdx] || focusables[ev.shiftKey ? focusables.length - 1 : 0];
                                    if (next) {
                                        next.focus();
                                        log('[js-tab-fix] moved focus ' + idx + ' -> ' + nextIdx + ' tag=' + next.tagName + ' id=' + next.id);
                                    }
                                }
                            }
                            if (ev.code === 'Enter') {
                                var ae = document.activeElement;
                                // Enter inside an input field — manually find
                                // the form's submit button and .click() it so
                                // the Blazor @onclick delegate fires. The
                                // native form-submit path goes through
                                // OnValidSubmit which DataAnnotations + the
                                // Catalyst event trap interferes with.
                                if (ae && ae.tagName === 'INPUT' && ae.form) {
                                    var submitBtn = ae.form.querySelector('button[type=""submit""]');
                                    if (submitBtn && !submitBtn.disabled) {
                                        ev.preventDefault();
                                        log('[js-enter-fix] triggering submit click via DOM .click().');
                                        submitBtn.click();
                                    }
                                }
                            }
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

            // Force WKWebView to claim first-responder so the host UIWindow
            // routes keyboard events here. Without this, keyboard input
            // on Catalyst can land on the containing UIView and never
            // reach the web content — one of several manifestations of
            // dotnet/maui#13934.
            wkWebView.BecomeFirstResponder();

            // Native UIView-layer click observer. If physical clicks
            // reach the WKWebView's UIView but DON'T reach the embedded
            // web content (the documented Catalyst trap), we'd see this
            // log fire but [js-pointerdown] / [js-mousedown] would not.
            var tapGesture = new UIKit.UITapGestureRecognizer((g) =>
            {
                var pt = g.LocationInView(wkWebView);
                Console.WriteLine($"[native-tap] uiview point=({pt.X:F1},{pt.Y:F1}) state={g.State}");
            });
            tapGesture.CancelsTouchesInView = false;
            tapGesture.DelaysTouchesBegan = false;
            tapGesture.DelaysTouchesEnded = false;
            wkWebView.AddGestureRecognizer(tapGesture);
            Console.WriteLine("[BlazorWebView] native UITapGestureRecognizer attached to WKWebView.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[BlazorWebView] FAIL hooking diagnostics: " + ex);
        }
#endif
    }
}
