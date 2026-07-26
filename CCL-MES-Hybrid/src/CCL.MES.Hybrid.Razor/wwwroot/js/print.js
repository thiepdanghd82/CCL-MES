// print.js — JS→native print bridge for the MAUI BlazorWebView (Mac Catalyst).
//
// Why this exists: window.print() is a NO-OP inside WKWebView on Mac
// Catalyst — it returns without ever showing a print panel. The native host
// (MainPage.OnBlazorWebViewInitialized) registers a WKScriptMessageHandler
// named "cclMesPrint"; posting to it drives UIPrintInteractionController with
// the WKWebView's ViewPrintFormatter, so the OS prints the LIVE DOM (WYSIWYG,
// with the app's @media print CSS) and shows the real print panel (A4/A3,
// orientation, scale, Save-as-PDF).
//
// The Blazor "Print" button takes the .NET path (IPrintService) directly;
// this bridge is the KEYBOARD path — Cmd/Ctrl+P and any code calling
// window.cclMesPrint.print() route to the exact same native panel.
window.cclMesPrint = (() => {
    function nativeAvailable() {
        try {
            return !!(window.webkit &&
                      window.webkit.messageHandlers &&
                      window.webkit.messageHandlers.cclMesPrint);
        } catch (e) { return false; }
    }

    function print() {
        if (nativeAvailable()) {
            try {
                window.webkit.messageHandlers.cclMesPrint.postMessage("print");
                return true;
            } catch (e) { /* fall through */ }
        }
        // Non-Catalyst host (e.g. dev browser): best-effort standard print.
        try { window.print(); } catch (e) { /* no-op in WKWebView */ }
        return false;
    }

    // Cmd/Ctrl+P → native panel instead of the WKWebView no-op. Only
    // intercept when the native bridge is present so a plain browser keeps
    // its own print dialog.
    window.addEventListener("keydown", (ev) => {
        if ((ev.metaKey || ev.ctrlKey) && (ev.key === "p" || ev.key === "P")) {
            if (nativeAvailable()) {
                ev.preventDefault();
                print();
            }
        }
    });

    return { print, isNativeAvailable: nativeAvailable };
})();
