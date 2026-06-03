#if MACCATALYST || IOS
using CCL.MES.Hybrid.Client.Hardware;

namespace CCL.MES.Hybrid.Platforms.MacCatalyst;

/// <summary>
/// Catalyst impl of <see cref="IDeviceSettingsLauncher"/>. Wraps the
/// platform-specific <see cref="CameraSettingsHelper"/> so the Razor
/// page doesn't need a Catalyst conditional.
/// </summary>
public sealed class MauiCatalystDeviceSettingsLauncher : IDeviceSettingsLauncher
{
    public Task<bool> OpenCameraSettingsAsync() => CameraSettingsHelper.TryOpenAsync();
}
#endif
