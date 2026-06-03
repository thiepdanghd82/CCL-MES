namespace CCL.MES.Shared.Devices;

/// <summary>
/// P10.3 W4 device-surface DTOs. The MAUI Hybrid client POSTs scan events
/// and periodic heartbeats per device-id so the server can render a
/// "kiosk health" admin view in later phases and reconstruct who scanned
/// what for forensic audit. GET returns a snapshot the operator-mode
/// admin tab will eventually consume — for now MAUI only uses it to
/// surface the server-confirmed last_seen on /hardware.
///
/// Device identity is the client-generated guid in
/// <see cref="Hardware.HardwareOptions"/>'s DeviceId surface
/// (<c>InMemoryDeviceModeService.DeviceId</c>) — stable per-install,
/// per-machine, NOT per-user. JWT carries the operator; device-id carries
/// the station. Together they let the audit log answer "operator X
/// scanned WO Y on station Z at time T".
/// </summary>
public sealed record ScanLogRequest
{
    /// <summary>Raw decoded payload (WO number, part code, anything the scanner produced).</summary>
    public string Payload { get; init; } = "";

    /// <summary>Mapper-resolved format e.g. "qr" / "code128" / "ean13". Free-form so
    /// W3 Windows can pass its ZXing tag while W2 Catalyst passes the AVFoundation tag.</summary>
    public string? Format { get; init; }

    /// <summary>Optional context — e.g. "wo-accept" / "ipqc-lookup" / "test". Helps
    /// audit timeline group events by intent rather than just payload.</summary>
    public string? Context { get; init; }

    /// <summary>Client-side scan timestamp (UTC). Server still stamps its own
    /// Timestamp on the audit row; this is for forensic clock-drift analysis.</summary>
    public DateTimeOffset ClientTimestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Friendly source label e.g. "FaceTime HD Camera" or "USB Honeywell 1900".</summary>
    public string? SourceDeviceLabel { get; init; }
}

public sealed record ScanLogResponse
{
    public Guid ScanId { get; init; }
    public DateTimeOffset ServerTimestamp { get; init; }
}

public sealed record HeartbeatRequest
{
    /// <summary>App-version semver e.g. "1.0.0". Lets server differentiate
    /// stations running old builds.</summary>
    public string? AppVersion { get; init; }

    /// <summary>Operator-visible device mode (interactive / kiosk-locked). Mirrors
    /// the kiosk lock state so server-side dashboards know if the station is
    /// supposed to be sequestered.</summary>
    public string? Mode { get; init; }

    /// <summary>Optional platform tag e.g. "MacCatalyst" / "WindowsAppSDK" / "Web".</summary>
    public string? Platform { get; init; }
}

public sealed record HeartbeatResponse
{
    public DateTimeOffset ServerTimestamp { get; init; }
}

public sealed record DeviceInfoResponse
{
    public string DeviceId { get; init; } = "";
    public DateTimeOffset? LastSeen { get; init; }
    public string? LastAppVersion { get; init; }
    public string? LastMode { get; init; }
    public string? LastPlatform { get; init; }
    public int ScanCountLast24h { get; init; }
}
