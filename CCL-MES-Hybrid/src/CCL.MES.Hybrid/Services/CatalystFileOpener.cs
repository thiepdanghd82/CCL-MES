using CCL.MES.Hybrid.Client.Files;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// P10.5e-1 — Mac Catalyst / iOS / WinUI file opener via MAUI's
/// <see cref="Launcher.Default"/>. Catalyst routes to QuickLook /
/// Preview / the default app for the file's UTType; WinUI calls
/// <c>Windows.System.Launcher.LaunchFileAsync</c>; both honour the
/// app-sandbox-safe download dir.
///
/// Per-platform note: Catalyst refuses to open files outside the app
/// container, so the download flow MUST save to
/// <see cref="GetSafeDownloadDirectory"/> first. We expose the dir as
/// a method instead of a static so future per-tenant / per-spec
/// overrides can plug in without re-wiring callers.
/// </summary>
public sealed class CatalystFileOpener : IFileOpener
{
    public async Task<bool> TryOpenAsync(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;
        if (!File.Exists(absolutePath)) return false;
        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                File = new ReadOnlyFile(absolutePath),
            });
            return true;
        }
        catch (FeatureNotSupportedException) { return false; }
        catch (PermissionException) { return false; }
    }

    public string GetSafeDownloadDirectory()
    {
        var dir = Path.Combine(FileSystem.Current.AppDataDirectory, "downloads");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
