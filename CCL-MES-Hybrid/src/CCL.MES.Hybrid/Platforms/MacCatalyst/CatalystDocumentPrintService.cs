#if MACCATALYST || IOS
using CCL.MES.Hybrid.Client.Printing;
using Foundation;
using UIKit;

namespace CCL.MES.Hybrid.Platforms.MacCatalyst;

/// <summary>
/// Mac Catalyst native print bridge. Presents the system print panel
/// (printer chooser + paper/scale/copies) for a ready-made PDF file via
/// <see cref="UIPrintInteractionController"/>. Printing the server spec-sheet
/// PDF (whose page = the chosen paper) yields a deterministic full-page-fit
/// print, unlike the WebView formatter which left landscape gaps.
/// </summary>
public sealed class CatalystDocumentPrintService : IDocumentPrintService
{
    public bool IsSupported => true;

    public Task<bool> PrintPdfFileAsync(string pdfFilePath, string? jobName = null)
    {
        var tcs = new TaskCompletionSource<bool>();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                var url = NSUrl.FromFilename(pdfFilePath);
                var controller = UIPrintInteractionController.SharedPrintController;

                var info = UIPrintInfo.PrintInfo;
                info.OutputType = UIPrintInfoOutputType.General;
                info.JobName = string.IsNullOrWhiteSpace(jobName) ? "CCL MES Spec" : jobName;
                controller.PrintInfo = info;

                // The PDF page already carries the paper size + orientation.
                controller.PrintingItem = url;
                controller.ShowsPageRange = true;

                controller.Present(true, (ctrl, completed, err) =>
                {
                    if (err is not null)
                        Console.WriteLine("[print] panel error: " + err.LocalizedDescription);
                    tcs.TrySetResult(completed);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[print] Catalyst print failed: " + ex);
                tcs.TrySetResult(false);
            }
        });
        return tcs.Task;
    }
}
#endif
