using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Process-scoped <see cref="IDeviceModeService"/> backed by a
/// dictionary in memory. Used by integration tests and as a fallback
/// in any host that doesn't ship a real Preferences impl (e.g. the
/// pure-net10.0 platform target before the MAUI Preferences API is
/// loaded). Production MAUI host overrides with
/// <c>MauiDeviceModeService</c>.
///
/// <para>
/// Passcode hashing in W1 uses a deliberately weak placeholder
/// (SHA-256 with a per-device salt embedded in the device id). W4
/// swaps in real Argon2id via libsodium-net. The interface contract
/// doesn't change.
/// </para>
/// </summary>
public sealed class InMemoryDeviceModeService : IDeviceModeService
{
    private DeviceMode _mode = DeviceMode.Interactive;
    private int _idleMinutes = 10;
    private string? _passcodeHash;
    private string _deviceId = Guid.CreateVersion7().ToString();

    public DeviceMode CurrentMode => _mode;
    public int IdleMinutes => _idleMinutes;
    public string DeviceId => _deviceId;
    public event Action? OnChange;

    public Task SetModeAsync(DeviceMode mode, CancellationToken ct = default)
    {
        _mode = mode;
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetIdleMinutesAsync(int minutes, CancellationToken ct = default)
    {
        _idleMinutes = minutes is < 1 or > 120 ? 10 : minutes;
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetPasscodeAsync(string? newPasscode, CancellationToken ct = default)
    {
        _passcodeHash = string.IsNullOrEmpty(newPasscode) ? null : Hash(newPasscode);
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public Task<bool> VerifyPasscodeAsync(string candidate, CancellationToken ct = default)
    {
        if (_passcodeHash is null) return Task.FromResult(false);
        return Task.FromResult(Hash(candidate) == _passcodeHash);
    }

    /// <summary>Test helper — let tests seed a device id deterministically.</summary>
    public void OverrideDeviceIdForTests(string deviceId) => _deviceId = deviceId;

    private string Hash(string raw)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(_deviceId + ":" + raw);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
