namespace CCL.MES.Hybrid.Client.Files;

/// <summary>
/// P10.5g — Cross-platform "Save As…" abstraction. Mac Catalyst impl
/// wraps <c>UIDocumentPickerViewController(forExporting:)</c> which is
/// the native macOS Save dialog (operator picks the folder + final
/// filename through the system UI). WinUI impl wraps
/// <c>FileSavePicker</c>. Test hosts wire the stub which returns
/// <see cref="SaveOutcome.Cancelled"/> so callers' UX path degrades to
/// "file kept in sandbox" rather than crashing.
///
/// The caller is responsible for downloading the file into the
/// sandbox-safe directory (<see cref="IFileOpener.GetSafeDownloadDirectory"/>)
/// FIRST, then handing the sandbox path to <see cref="SaveAsync"/>. The
/// impl copies the bytes out to the operator-chosen location; the
/// original sandbox copy stays put so a follow-up
/// <see cref="IFileOpener.TryOpenAsync"/> works even when the operator
/// saved the file to an external volume that QuickLook can't reach.
/// </summary>
public interface IFileSaver
{
    /// <summary>
    /// Open the native Save dialog. Returns an outcome describing
    /// whether the operator saved (with the absolute destination path)
    /// or cancelled. NEVER throws on operator cancel.
    /// </summary>
    /// <param name="sourceFilePath">Absolute path to a file already
    /// sitting in the app sandbox; the impl reads bytes from here and
    /// writes them out to the operator's chosen location.</param>
    /// <param name="suggestedFileName">Default name shown in the Save
    /// dialog. Caller should pass the server-stamped filename
    /// (NpiSpecLibrary_…xlsx / SpecSheet_…pdf) so the operator can keep
    /// timestamps consistent with the audit-log filename column.</param>
    Task<SaveOutcome> SaveAsync(
        string sourceFilePath,
        string suggestedFileName,
        CancellationToken ct = default);
}

/// <summary>Outcome of a <see cref="IFileSaver.SaveAsync"/> call. Two
/// shapes: Saved (with the absolute destination path the operator
/// chose) or Cancelled (operator dismissed the dialog).</summary>
public sealed record SaveOutcome
{
    public bool Saved { get; init; }
    public string? DestinationPath { get; init; }
    public string? Error { get; init; }

    public static SaveOutcome Cancelled => new() { Saved = false };
    public static SaveOutcome Success(string path) => new() { Saved = true, DestinationPath = path };
    public static SaveOutcome Failed(string message) => new() { Saved = false, Error = message };
}

/// <summary>Stub for tests + non-MAUI hosts — always returns
/// <see cref="SaveOutcome.Cancelled"/>. Callers gracefully fall back to
/// the sandbox path so the operator still sees a "file ready" banner
/// even when the platform can't host a Save dialog.</summary>
public sealed class StubFileSaver : IFileSaver
{
    public Task<SaveOutcome> SaveAsync(
        string sourceFilePath, string suggestedFileName, CancellationToken ct = default)
        => Task.FromResult(SaveOutcome.Cancelled);
}
