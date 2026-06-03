namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Platform-bridge to open the host OS settings pages from a Razor
/// page. Used today by the camera-permission deny banner — operator
/// taps "Mở Settings" → settings app opens at Privacy → Camera (on
/// Catalyst) or Settings → Apps → CCL MES (on iOS) or the equivalent
/// privacy pane on Windows. Returning false means the launcher
/// couldn't open ANY settings URL — UI should fall back to printed
/// instructions ("Apple Menu → System Settings → Privacy &amp; Security
/// → Camera").
///
/// <para>
/// W2 ships the Catalyst impl
/// (<c>MauiCatalystDeviceSettingsLauncher</c> in the host project).
/// Stub for cross-platform + test targets returns false so the UI
/// behaves the same on platforms that don't have the deep-link wired
/// yet.
/// </para>
/// </summary>
public interface IDeviceSettingsLauncher
{
    Task<bool> OpenCameraSettingsAsync();
}

/// <summary>Default no-op for test + non-MAUI hosts.</summary>
public sealed class StubDeviceSettingsLauncher : IDeviceSettingsLauncher
{
    public Task<bool> OpenCameraSettingsAsync() => Task.FromResult(false);
}
