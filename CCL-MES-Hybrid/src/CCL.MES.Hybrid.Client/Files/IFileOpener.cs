namespace CCL.MES.Hybrid.Client.Files;

/// <summary>
/// P10.5e-1 — Cross-platform "open this file in its default viewer"
/// abstraction. Mac Catalyst impl wraps
/// <c>Launcher.Default.OpenAsync(new OpenFileRequest{...})</c> which
/// hands the file off to QuickLook / Preview / the default associated
/// app. Test hosts wire the stub which returns false.
///
/// The downloaded file MUST live in the app sandbox before opening —
/// Mac Catalyst Launcher refuses paths outside the app's container.
/// Callers download to <see cref="GetSafeDownloadDirectory"/> first.
/// </summary>
public interface IFileOpener
{
    /// <summary>Try to open the file via the OS default handler.
    /// Returns true on success, false on operator cancel / no-handler /
    /// unsupported platform.</summary>
    Task<bool> TryOpenAsync(string absolutePath);

    /// <summary>App-sandbox-safe directory operators can write to and
    /// the OS can read back via Launcher. On Catalyst this resolves to
    /// <c>FileSystem.AppDataDirectory/downloads/</c>; on test hosts it
    /// resolves to a per-process tmp folder.</summary>
    string GetSafeDownloadDirectory();
}

/// <summary>Stub for tests + non-MAUI hosts — never opens anything,
/// always returns false. Caller's UX path degrades to "file was saved
/// to disk" rather than crashing.</summary>
public sealed class StubFileOpener : IFileOpener
{
    public Task<bool> TryOpenAsync(string absolutePath) => Task.FromResult(false);

    public string GetSafeDownloadDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccl-mes-downloads");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
