namespace CCL.MES.Hybrid.Client.Printing;

/// <summary>
/// P11.x — Native "print the current WebView content" abstraction.
///
/// Why: <c>window.print()</c> is a NO-OP inside a MAUI BlazorWebView /
/// WKWebView on Mac Catalyst — the JS call returns without ever showing a
/// print panel. So the Mac Catalyst impl (<c>CatalystPrintService</c>)
/// drives <c>UIPrintInteractionController</c> with the WKWebView's own
/// <c>ViewPrintFormatter</c>: the OS renders the LIVE DOM (WYSIWYG — the
/// exact spec sheet the operator sees, with the app's <c>@media print</c>
/// CSS applied) and shows the native print panel where the operator picks
/// A4/A3, portrait/landscape, scale, and "Save as PDF".
///
/// Non-Catalyst hosts (Windows, test harness) wire <see cref="StubPrintService"/>
/// which reports <see cref="IsNativePrintSupported"/> = false, so callers
/// fall back to the server-rendered MigraDoc sheet PDF.
/// </summary>
public interface IPrintService
{
    /// <summary>True when this host can present a native OS print panel for
    /// the WebView DOM (Mac Catalyst / iOS). False on Windows + test hosts —
    /// the caller should fall back to the MigraDoc download path.</summary>
    bool IsNativePrintSupported { get; }

    /// <summary>
    /// Present the native OS print panel for the CURRENT WebView content
    /// (WYSIWYG). The panel lets the operator choose paper size, orientation,
    /// scale, and Save-as-PDF. Never throws on operator cancel.
    /// </summary>
    /// <param name="jobName">Optional print-job name shown in the OS panel /
    /// PDF metadata (e.g. the spec ref + revision).</param>
    /// <returns>
    /// <c>true</c> when the native panel was presented (INCLUDING the case
    /// where the operator then cancelled it — the request was handled
    /// natively). <c>false</c> only when native print is unavailable on this
    /// host (stub / no WebView) — the caller should then fall back to the
    /// MigraDoc download path.
    /// </returns>
    Task<bool> PrintCurrentViewAsync(string? jobName = null);
}

/// <summary>Stub for tests + non-MAUI hosts — native print is never
/// available, so callers degrade to the server MigraDoc sheet PDF. Never
/// throws.</summary>
public sealed class StubPrintService : IPrintService
{
    public bool IsNativePrintSupported => false;

    public Task<bool> PrintCurrentViewAsync(string? jobName = null) => Task.FromResult(false);
}
