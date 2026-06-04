using CCL.MES.Hybrid.Client.Files;
#if MACCATALYST || IOS
using Foundation;
using UIKit;
#endif

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// P10.5g — Mac Catalyst Save-As impl backed by
/// <c>UIDocumentPickerViewController(forExporting:)</c>. The picker is
/// the native macOS Save dialog — operator sees the standard folder
/// tree, can rename the file, can pick a non-sandbox location (Files
/// app exports the bytes there via the security-scoped URL handed to
/// the delegate).
///
/// We rely on the caller having already written the bytes to the app
/// sandbox (via <see cref="IFileOpener.GetSafeDownloadDirectory"/>); the
/// system picker then COPIES (asCopy: true) the source file to the
/// operator-chosen location. Keeping the sandbox copy lets a follow-up
/// <see cref="IFileOpener.TryOpenAsync"/> succeed even when the operator
/// saved to an external volume the system viewer can't reach back to.
///
/// Cancel / no-handler / platform-not-Catalyst paths all return
/// <see cref="SaveOutcome.Cancelled"/> so the UI shell can show a
/// "saved to sandbox" hint instead of a stack trace.
/// </summary>
public sealed class CatalystFileSaver : IFileSaver
{
    public async Task<SaveOutcome> SaveAsync(
        string sourceFilePath, string suggestedFileName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            return SaveOutcome.Failed("Source file missing — download did not complete.");

#if MACCATALYST || IOS
        // UIDocumentPickerViewController(forExporting: …) infers the UTI
        // from the source file's extension on disk, so we don't pass a
        // separate UTType — just make sure the suggested filename ends
        // with the right extension below.
        var srcUrl = NSUrl.FromFilename(sourceFilePath);
        if (!string.IsNullOrWhiteSpace(suggestedFileName) &&
            !string.Equals(Path.GetFileName(sourceFilePath), suggestedFileName, StringComparison.Ordinal))
        {
            var renamed = Path.Combine(Path.GetDirectoryName(sourceFilePath)!, suggestedFileName);
            try
            {
                if (File.Exists(renamed)) File.Delete(renamed);
                File.Copy(sourceFilePath, renamed, overwrite: true);
                srcUrl = NSUrl.FromFilename(renamed);
            }
            catch
            {
                // Fall back to the original file — picker just defaults to its
                // basename. Operator can rename in-dialog if needed.
            }
        }

        // The picker MUST be presented on the UI thread; the wrapping
        // TaskCompletionSource bridges back to the awaiting caller.
        var tcs = new TaskCompletionSource<SaveOutcome>();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            try
            {
                var picker = new UIDocumentPickerViewController(new[] { srcUrl }, asCopy: true)
                {
                    ShouldShowFileExtensions = true,
                };
                picker.Delegate = new SaveDialogDelegate(tcs);
                picker.ModalPresentationStyle = UIModalPresentationStyle.FormSheet;

                var root = GetRootViewController();
                if (root is null)
                {
                    tcs.TrySetResult(SaveOutcome.Failed("No root view controller available."));
                    return;
                }
                root.PresentViewController(picker, animated: true, completionHandler: null);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult(SaveOutcome.Failed(ex.Message));
            }
        });

        using (ct.Register(() => tcs.TrySetResult(SaveOutcome.Cancelled)))
        {
            return await tcs.Task;
        }
#else
        // Non-Catalyst (Windows): the WinUI Save dialog needs a different
        // bridge (FileSavePicker). Until that lands, callers see Cancelled
        // and degrade to "file kept in sandbox" UX — same as the stub.
        return SaveOutcome.Cancelled;
#endif
    }

#if MACCATALYST || IOS
    private static UIViewController? GetRootViewController()
    {
        var scenes = UIApplication.SharedApplication.ConnectedScenes;
        foreach (var s in scenes)
        {
            if (s is UIWindowScene ws && ws.Windows.Length > 0)
            {
                var window = ws.Windows.FirstOrDefault(w => w.IsKeyWindow) ?? ws.Windows[0];
                var root = window.RootViewController;
                while (root?.PresentedViewController is { } presented)
                    root = presented;
                return root;
            }
        }
        return null;
    }

    /// <summary>UIKit delegate that captures the operator's chosen URL
    /// (Saved) or dismissal (Cancelled). Both bridge back to the
    /// awaiting caller via the supplied <see cref="TaskCompletionSource{T}"/>.</summary>
    private sealed class SaveDialogDelegate : UIDocumentPickerDelegate
    {
        private readonly TaskCompletionSource<SaveOutcome> _tcs;
        public SaveDialogDelegate(TaskCompletionSource<SaveOutcome> tcs) => _tcs = tcs;

        // iOS 11+ multi-doc shape — the only one Catalyst surfaces.
        public override void DidPickDocument(UIDocumentPickerViewController controller, NSUrl[] urls)
        {
            if (urls is null || urls.Length == 0)
            {
                _tcs.TrySetResult(SaveOutcome.Cancelled);
                return;
            }
            var url = urls[0];
            var path = url.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                _tcs.TrySetResult(SaveOutcome.Failed("Document picker returned an empty path."));
                return;
            }
            _tcs.TrySetResult(SaveOutcome.Success(path));
        }

        public override void WasCancelled(UIDocumentPickerViewController controller)
            => _tcs.TrySetResult(SaveOutcome.Cancelled);
    }
#endif
}
