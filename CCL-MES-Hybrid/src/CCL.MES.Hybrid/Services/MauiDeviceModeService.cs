using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Shared.Hardware;
using Microsoft.Maui.Storage;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// MAUI host impl of <see cref="IDeviceModeService"/> — persists to
/// <see cref="Preferences"/> (per-app key/value store, abstracts to
/// <c>NSUserDefaults</c> on Catalyst/iOS and the registry on Windows).
/// Replaces the in-memory client-side default at MAUI app startup so
/// settings survive relaunch.
///
/// <para>
/// Storage key shape mirrors the P10.3 plan §5:
/// </para>
/// <list type="bullet">
/// <item><c>device.id</c> — GUID v7, lazily created on first read.</item>
/// <item><c>device.mode</c> — int matching <see cref="DeviceMode"/>.</item>
/// <item><c>device.idle.minutes</c> — int 1..120 (clamped on write).</item>
/// <item><c>device.passcode.hash</c> — hex SHA-256 of (deviceId + passcode).
///   W1 placeholder; W4 swaps in Argon2id.</item>
/// </list>
/// </summary>
public sealed class MauiDeviceModeService : IDeviceModeService
{
    private const string KeyDeviceId = "device.id";
    private const string KeyMode = "device.mode";
    private const string KeyIdleMinutes = "device.idle.minutes";
    private const string KeyPasscodeHash = "device.passcode.hash";

    private readonly object _lock = new();
    private DeviceMode _modeCache;
    private int _idleMinutesCache;
    private string _deviceIdCache;

    public MauiDeviceModeService()
    {
        // Lazy-init device.id at construction so every subsequent read is
        // cheap + so two threads racing on first launch don't write two
        // different GUIDs.
        _deviceIdCache = Preferences.Default.Get(KeyDeviceId, string.Empty);
        if (string.IsNullOrEmpty(_deviceIdCache))
        {
            _deviceIdCache = Guid.CreateVersion7().ToString();
            Preferences.Default.Set(KeyDeviceId, _deviceIdCache);
        }

        _modeCache = (DeviceMode)Preferences.Default.Get(KeyMode, (int)DeviceMode.Interactive);
        _idleMinutesCache = Preferences.Default.Get(KeyIdleMinutes, 10);
    }

    public DeviceMode CurrentMode => _modeCache;
    public int IdleMinutes => _idleMinutesCache;
    public string DeviceId => _deviceIdCache;
    public event Action? OnChange;

    public Task SetModeAsync(DeviceMode mode, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _modeCache = mode;
            Preferences.Default.Set(KeyMode, (int)mode);
        }
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetIdleMinutesAsync(int minutes, CancellationToken ct = default)
    {
        var clamped = minutes is < 1 or > 120 ? 10 : minutes;
        lock (_lock)
        {
            _idleMinutesCache = clamped;
            Preferences.Default.Set(KeyIdleMinutes, clamped);
        }
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public Task SetPasscodeAsync(string? newPasscode, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(newPasscode))
            Preferences.Default.Remove(KeyPasscodeHash);
        else
            Preferences.Default.Set(KeyPasscodeHash, Hash(newPasscode));
        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public Task<bool> VerifyPasscodeAsync(string candidate, CancellationToken ct = default)
    {
        var stored = Preferences.Default.Get(KeyPasscodeHash, string.Empty);
        if (string.IsNullOrEmpty(stored)) return Task.FromResult(false);
        return Task.FromResult(Hash(candidate) == stored);
    }

    private string Hash(string raw)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(_deviceIdCache + ":" + raw);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
