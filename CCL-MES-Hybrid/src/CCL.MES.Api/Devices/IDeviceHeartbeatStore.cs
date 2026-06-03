using CCL.MES.Shared.Devices;

namespace CCL.MES.Api.Devices;

/// <summary>
/// P10.3 W4 — process-scoped in-memory store of the last
/// <see cref="HeartbeatRequest"/> seen per device id. Heartbeats are
/// high frequency (once per minute typical) so persisting every ping
/// to the audit log would explode the table; instead we keep a
/// live snapshot in memory + emit an audit row only on the FIRST ping
/// after an idle gap (reconnection event), which is what an admin
/// dashboard actually wants to surface.
///
/// Singleton lifetime — the store survives across requests. Process
/// restart clears it (acceptable: heartbeat cadence rebuilds the map in
/// under a minute). Replacement with a durable cache (Redis / SQLite
/// table on a new database) is a P10.4 concern; W4 keeps the surface
/// here so the API contract on the wire is already correct.
/// </summary>
public interface IDeviceHeartbeatStore
{
    /// <summary>Returns the previous snapshot (if any) BEFORE applying the new one,
    /// so the caller can decide whether to emit a DEVICE_RECONNECT audit row.</summary>
    DeviceHeartbeatSnapshot? RecordHeartbeat(string deviceId, HeartbeatRequest req, DateTimeOffset serverTimestamp);

    /// <summary>Increment the scan counter for the last 24-hour window. Idempotent
    /// per scanId so accidental double-deliveries don't bloat the counter.</summary>
    void RecordScan(string deviceId, Guid scanId, DateTimeOffset serverTimestamp);

    /// <summary>Snapshot for GET /devices/{id}. Null when device never connected.</summary>
    DeviceHeartbeatSnapshot? Get(string deviceId);
}

public sealed record DeviceHeartbeatSnapshot(
    string DeviceId,
    DateTimeOffset LastSeen,
    string? LastAppVersion,
    string? LastMode,
    string? LastPlatform,
    int ScanCountLast24h);
