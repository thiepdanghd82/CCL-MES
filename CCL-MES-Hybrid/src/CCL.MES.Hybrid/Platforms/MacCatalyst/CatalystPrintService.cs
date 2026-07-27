#if MACCATALYST || IOS
using CCL.MES.Hybrid.Client.Printing;
using Foundation;
using UIKit;
using WebKit;

namespace CCL.MES.Hybrid.Platforms.MacCatalyst;

/// <summary>
/// Mac Catalyst impl of <see cref="IPrintService"/>. Drives the OS print
/// panel over the LIVE WebView DOM via
/// <see cref="UIPrintInteractionController"/> + the WKWebView's own
/// <see cref="UIView.ViewPrintFormatter"/>.
///
/// This is the WYSIWYG path: the OS rasterises the exact DOM the operator
/// is looking at (with the app's <c>@media print</c> CSS applied — chrome
/// hidden, sheet expanded to the page, tables tuned to fit), so the printed
/// output / saved PDF matches the on-screen Spec Full sheet. The native
/// panel exposes paper size (A4/A3), orientation, scale, copies, and the
/// system "Save as PDF" — none of which <c>window.print()</c> (a no-op in
/// WKWebView) could offer.
///
/// Default orientation is Landscape because the Spec Full sheet's print-
/// process table is wide (~18 columns); the operator can flip it in the
/// panel. Presentation is marshalled to the UI thread and never throws to
/// the caller — a failure returns <c>false</c> so the Razor layer can fall
/// back to the server MigraDoc PDF.
/// </summary>
public sealed class CatalystPrintService : IPrintService
{
    public bool IsNativePrintSupported => true;

    public Task<bool> PrintCurrentViewAsync(string? jobName = null)
    {
        var webView = CatalystWebViewHolder.WebView;
        if (webView is null)
        {
            // The WebView ref was never captured (init hook did not run) —
            // report "not available" so the caller falls back to MigraDoc.
            Console.WriteLine("[print] WKWebView ref is null — cannot present native print panel.");
            return Task.FromResult(false);
        }
        return PresentAsync(webView, jobName);
    }

    /// <summary>
    /// Present the native print panel for <paramref name="webView"/>. Shared
    /// by the DI service AND the <c>cclMesPrint</c> JS message handler so a
    /// Cmd/Ctrl+P (or <c>window.cclMesPrint.print()</c>) takes the exact same
    /// native path as the Blazor Print button. Resolves <c>true</c> when the
    /// panel was presented (operator cancel included), <c>false</c> on error.
    /// </summary>
    public static Task<bool> PresentAsync(WKWebView webView, string? jobName)
    {
        var tcs = new TaskCompletionSource<bool>();

        void Present()
        {
            try
            {
                var controller = UIPrintInteractionController.SharedPrintController;

                var info = UIPrintInfo.PrintInfo;
                info.OutputType = UIPrintInfoOutputType.General; // full colour, graphics
                info.Orientation = UIPrintInfoOrientation.Landscape; // wide spec sheet
                info.JobName = string.IsNullOrWhiteSpace(jobName) ? "CCL-MES Spec Sheet" : jobName;
                controller.PrintInfo = info;

                // Print the WebView's own view formatter — the OS renders the
                // live DOM (WYSIWYG). @media print CSS decides what shows.
                controller.PrintFormatter = webView.ViewPrintFormatter;
                controller.ShowsNumberOfCopies = true;
                controller.ShowsPaperSelectionForLoadedPapers = true;

                controller.Present(animated: true, (ctrl, completed, error) =>
                {
                    if (error is not null)
                        Console.WriteLine("[print] print panel error: " + error.LocalizedDescription);
                    // Panel WAS presented natively regardless of completed/
                    // cancelled → the request was handled here (no fallback).
                    tcs.TrySetResult(true);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[print] FAIL presenting native print panel: " + ex);
                tcs.TrySetResult(false);
            }
        }

        if (MainThread.IsMainThread) Present();
        else MainThread.BeginInvokeOnMainThread(Present);

        return tcs.Task;
    }
}
#endif
