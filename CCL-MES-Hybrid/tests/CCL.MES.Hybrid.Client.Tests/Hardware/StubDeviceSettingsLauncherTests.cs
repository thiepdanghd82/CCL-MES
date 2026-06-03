using CCL.MES.Hybrid.Client.Hardware;

namespace CCL.MES.Hybrid.Client.Tests.Hardware;

public sealed class StubDeviceSettingsLauncherTests
{
    [Fact]
    public async Task OpenCameraSettings_returns_false()
    {
        var launcher = new StubDeviceSettingsLauncher();
        Assert.False(await launcher.OpenCameraSettingsAsync());
    }

    /// <summary>
    /// The Catalyst impl returns true on success. The /hardware
    /// page distinguishes false (couldn't open) from true (opened)
    /// so the UI can show the printed-instructions fallback only
    /// when the launcher couldn't open ANY settings URL.
    /// </summary>
    [Fact]
    public async Task Stub_always_returns_false_so_UI_shows_manual_path()
    {
        var launcher = new StubDeviceSettingsLauncher();
        for (var i = 0; i < 5; i++)
            Assert.False(await launcher.OpenCameraSettingsAsync());
    }
}
