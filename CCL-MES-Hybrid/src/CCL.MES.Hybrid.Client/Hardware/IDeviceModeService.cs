using CCL.MES.Shared.Hardware;

namespace CCL.MES.Hybrid.Client.Hardware;

/// <summary>
/// Per-device mode (<see cref="DeviceMode"/>) — persisted locally,
/// surfaced on the <c>/mode</c> page. REAL impl in W1 already:
/// the mode flag is fundamentally local state (it describes how the
/// app on THIS hardware behaves) and has no platform-specific
/// surface beyond key/value persistence. The MAUI host wires
/// <c>MauiDeviceModeService</c> against <c>Preferences</c>; tests
/// wire <c>InMemoryDeviceModeService</c>.
///
/// <para>
/// Idle threshold + lock-screen passcode (the other per-device
/// settings touched on /mode) live alongside the mode in the same
/// service so reading/writing kiosk config is one round-trip to
/// <c>Preferences</c>.
/// </para>
/// </summary>
public interface IDeviceModeService
{
    /// <summary>
    /// Current mode for THIS device. Read cheap — backed by an
    /// in-memory snapshot loaded once at boot, refreshed on Set.
    /// </summary>
    DeviceMode CurrentMode { get; }

    /// <summary>
    /// Idle threshold in minutes for Kiosk mode. Default 10.
    /// Range 1..120; values outside the range fall back to default
    /// silently. Has no effect in Interactive mode.
    /// </summary>
    int IdleMinutes { get; }

    /// <summary>
    /// Stable per-install device id (GUID v7). Generated lazily on
    /// first read + persisted. Sent with every scan-log entry and
    /// every heartbeat so the server can attribute hardware events
    /// to a station even when multiple users log in across a shift.
    /// </summary>
    string DeviceId { get; }

    Task SetModeAsync(DeviceMode mode, CancellationToken ct = default);
    Task SetIdleMinutesAsync(int minutes, CancellationToken ct = default);

    /// <summary>
    /// Set or clear the device passcode used to unlock the kiosk
    /// lock screen. Pass null to clear. The passcode is stored as
    /// an Argon2 hash — never raw — keyed by the device id so it
    /// can't be ported by copying the SQLite/Preferences blob to
    /// another machine. W1 stores a placeholder; W4 swaps in real
    /// Argon2 from libsodium.
    /// </summary>
    Task SetPasscodeAsync(string? newPasscode, CancellationToken ct = default);

    /// <summary>
    /// Verify a candidate passcode against the stored hash. Returns
    /// false when no passcode is set (kiosk lock screen treats this
    /// as "no protection" + the UI hides the unlock prompt).
    /// </summary>
    Task<bool> VerifyPasscodeAsync(string candidate, CancellationToken ct = default);

    /// <summary>Fires after any setter completes — pages subscribe to re-render.</summary>
    event Action? OnChange;
}
