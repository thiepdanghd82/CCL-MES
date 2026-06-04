using CCL.MES.Hybrid.Client.Files;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// P10.5c-2 — Native file picker bridge for the MAUI host. Wraps
/// <see cref="FilePicker.Default"/> which dispatches to
/// <c>UIDocumentPickerViewController</c> on Mac Catalyst + iOS, and to
/// <c>IFileOpenPicker</c> on WinUI. The xlsx filter uses the canonical
/// UTType (<c>org.openxmlformats.spreadsheetml.sheet</c>) so the picker
/// greys out non-matching files at OS level — operator can't pick a
/// renamed .docx by accident.
///
/// The returned stream is OpenReadAsync-backed and read-once: callers
/// must stream directly into the multipart upload without buffering
/// (Lesson D-5b: <c>OpenReadStream</c> → <c>content</c> directly). We
/// expose the seekable length when the platform reports it, falling
/// back to -1 so the upload helper uses chunked transfer.
/// </summary>
public sealed class CatalystFilePicker : IFilePickerService
{
    public async Task<PickedFile?> PickXlsxAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var options = new PickOptions
        {
            PickerTitle = "Chọn file Spec (.xlsx)",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                // UTType for xlsx — works on Catalyst + iOS. Sandboxed
                // file pickers won't surface files outside this type.
                { DevicePlatform.MacCatalyst, new[] { "org.openxmlformats.spreadsheetml.sheet" } },
                { DevicePlatform.iOS,        new[] { "org.openxmlformats.spreadsheetml.sheet" } },
                // WinUI takes plain extension strings (with the leading dot).
                { DevicePlatform.WinUI,      new[] { ".xlsx" } },
                // Android — keep in shape so future Phase-10++ ports stay
                // green without re-versioning this abstraction.
                { DevicePlatform.Android,    new[] { "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" } },
            }),
        };

        FileResult? result;
        try
        {
            result = await FilePicker.Default.PickAsync(options);
        }
        catch (PermissionException)
        {
            // macOS sandbox may refuse certain locations; treat as
            // operator-cancelled so the modal banner shows the standard
            // "no file picked" hint instead of an alarming stack trace.
            return null;
        }
        catch (FeatureNotSupportedException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            // Operator dismissed the system picker — same as Cancel.
            return null;
        }

        if (result is null) return null;

        var stream = await result.OpenReadAsync();
        long length = stream.CanSeek ? stream.Length : -1;
        return new PickedFile(result.FileName, length, stream);
    }

    /// <summary>
    /// P10.5e-1 — Generic drawing/file picker keyed by extension
    /// allowlist. Delegates to the pure
    /// <see cref="FilePickerExtensionMap"/> for per-platform type
    /// translation so the platform vs allowlist coupling stays small +
    /// xUnit-coverable. Same operator-cancel / sandbox semantics as
    /// <see cref="PickXlsxAsync"/>.
    /// </summary>
    public async Task<PickedFile?> PickFileAsync(
        IReadOnlyList<string> allowedExtensions, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (allowedExtensions is null || allowedExtensions.Count == 0)
            return null;

        var catalystTypes = FilePickerExtensionMap.MapCatalyst(allowedExtensions);
        var windowsTypes = FilePickerExtensionMap.MapWindows(allowedExtensions);
        var androidTypes = FilePickerExtensionMap.MapAndroid(allowedExtensions);

        var options = new PickOptions
        {
            PickerTitle = "Chọn bản vẽ",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.MacCatalyst, catalystTypes },
                { DevicePlatform.iOS,        catalystTypes },
                { DevicePlatform.WinUI,      windowsTypes },
                { DevicePlatform.Android,    androidTypes },
            }),
        };

        FileResult? result;
        try
        {
            result = await FilePicker.Default.PickAsync(options);
        }
        catch (PermissionException) { return null; }
        catch (FeatureNotSupportedException) { return null; }
        catch (TaskCanceledException) { return null; }

        if (result is null) return null;
        var stream = await result.OpenReadAsync();
        long length = stream.CanSeek ? stream.Length : -1;
        return new PickedFile(result.FileName, length, stream);
    }
}
