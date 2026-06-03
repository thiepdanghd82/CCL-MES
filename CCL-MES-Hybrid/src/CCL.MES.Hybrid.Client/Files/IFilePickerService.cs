namespace CCL.MES.Hybrid.Client.Files;

/// <summary>
/// P10.5c-2 — Platform-agnostic file picker abstraction. Real impls live
/// in the MAUI host (FilePicker.Default wraps UIDocumentPickerViewController
/// on Catalyst, OpenFileDialog on Windows). Test hosts wire the stub
/// which always returns null (operator-cancelled).
///
/// Caller is responsible for disposing <see cref="PickedFile.Content"/>.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Open the native file picker filtered to .xlsx (UTType
    /// <c>org.openxmlformats.spreadsheetml.sheet</c> on Catalyst).
    /// Returns null when the operator cancels.
    ///
    /// The returned <see cref="PickedFile.Content"/> is a read-once
    /// stream opened lazily — NOT buffered into memory. Caller streams
    /// directly into the multipart upload to keep the 5–10 MB xlsx off
    /// the heap (Lesson D-5b — OpenReadStream → content directly).
    /// </summary>
    Task<PickedFile?> PickXlsxAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of a successful pick. <see cref="Content"/> is owned by the
/// caller; dispose it when the upload finishes.
/// </summary>
public sealed class PickedFile : IAsyncDisposable
{
    public string FileName { get; }
    public long Length { get; }
    public Stream Content { get; }

    public PickedFile(string fileName, long length, Stream content)
    {
        FileName = fileName;
        Length = length;
        Content = content;
    }

    public async ValueTask DisposeAsync()
    {
        await Content.DisposeAsync();
    }
}
