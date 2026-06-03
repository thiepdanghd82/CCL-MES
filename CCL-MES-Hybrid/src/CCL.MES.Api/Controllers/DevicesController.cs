using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Devices;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.3 W4 — kiosk/device surface for the MAUI Hybrid client. Three
/// endpoints make up the minimal contract:
///
///   POST /devices/{id}/scan-log
///     Audit-emit every successful decode the operator triggered on
///     this station. Action = DEVICE_SCAN. Detail JSON: { payload,
///     format, context, source_label, client_ts }. Returns the
///     server-stamped <see cref="ScanLogResponse.ScanId"/> + server
///     timestamp so the client can match its local log entry to the
///     server-side audit row (forensic correlation).
///
///   POST /devices/{id}/heartbeat
///     Periodic liveness ping. Updates the in-memory snapshot only —
///     does NOT write an audit row on every call. Emits DEVICE_RECONNECT
///     ONLY when the previous snapshot is older than 5 minutes (or
///     missing), so admin dashboards can render reconnect events without
///     the table exploding.
///
///   GET /devices/{id}
///     Snapshot read. Returns the last known mode/version/platform +
///     24h scan count. 404 when the station hasn't connected yet (so
///     the UI distinguishes "never" from "offline").
///
/// Authorization — every endpoint inherits the FallbackPolicy
/// (RequireAuthenticatedUser) and accepts any role: operators need
/// scan-log + heartbeat from the floor, supervisors + admins read the
/// device snapshot from /hardware. We deliberately do NOT lock these
/// down to admin-only — keeping operators free to use the scanner is
/// the whole point of W4.
///
/// Device-id forgery defence: we trust the path-segment id matches the
/// client's actual install. The MAUI install generates a guid v7 device
/// id on first launch and persists it in MAUI Preferences (W4 idle for
/// now, MauiDeviceModeService landing). An attacker forging a device
/// id can only pollute the snapshot for someone else's station — they
/// cannot read another station's data (snapshots are scoped by id, no
/// cross-tenancy concerns yet), and the audit row carries the JWT actor
/// so accountability stays intact.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/devices")]
public sealed class DevicesController : ControllerBase
{
    private const string AuditDeviceScan = "DEVICE_SCAN";
    private const string AuditDeviceReconnect = "DEVICE_RECONNECT";
    private static readonly TimeSpan ReconnectGap = TimeSpan.FromMinutes(5);

    private readonly IDeviceHeartbeatStore _store;
    private readonly IAuditWriter _audit;

    public DevicesController(IDeviceHeartbeatStore store, IAuditWriter audit)
    {
        _store = store;
        _audit = audit;
    }

    [HttpGet("{deviceId}")]
    public ActionResult<DeviceInfoResponse> Get(string deviceId)
    {
        if (!IsValidDeviceId(deviceId))
            return BadRequest(ApiError.Of("device.invalid_id", "Device id must be 8-128 chars, alphanumeric + dash."));

        var snap = _store.Get(deviceId);
        if (snap is null) return NotFound(ApiError.Of("device.not_seen", "Device has not connected yet."));

        return Ok(new DeviceInfoResponse
        {
            DeviceId = snap.DeviceId,
            LastSeen = snap.LastSeen,
            LastAppVersion = snap.LastAppVersion,
            LastMode = snap.LastMode,
            LastPlatform = snap.LastPlatform,
            ScanCountLast24h = snap.ScanCountLast24h,
        });
    }

    [HttpPost("{deviceId}/scan-log")]
    public async Task<ActionResult<ScanLogResponse>> LogScan(string deviceId, [FromBody] ScanLogRequest req)
    {
        if (!IsValidDeviceId(deviceId))
            return BadRequest(ApiError.Of("device.invalid_id", "Device id must be 8-128 chars, alphanumeric + dash."));
        if (req is null || string.IsNullOrWhiteSpace(req.Payload))
            return BadRequest(ApiError.Of("scan.empty_payload", "Scan payload is required."));

        var scanId = Guid.NewGuid();
        var serverTs = DateTimeOffset.UtcNow;
        _store.RecordScan(deviceId, scanId, serverTs);

        // Audit row — actor from JWT, detail trimmed by ApiAuditWriter (4 KB max).
        var detail = JsonSerializer.Serialize(new
        {
            scan_id = scanId,
            device_id = deviceId,
            payload = Truncate(req.Payload, 512),
            format = req.Format,
            context = req.Context,
            source_label = req.SourceDeviceLabel,
            client_ts = req.ClientTimestamp,
        });
        await _audit.EmitAsync(
            action: AuditDeviceScan,
            actor: User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
            actorRole: User.FindFirstValue(ClaimTypes.Role) ?? "",
            targetType: "Device",
            targetId: deviceId,
            detail: detail);

        return Ok(new ScanLogResponse { ScanId = scanId, ServerTimestamp = serverTs });
    }

    [HttpPost("{deviceId}/heartbeat")]
    public async Task<ActionResult<HeartbeatResponse>> Heartbeat(string deviceId, [FromBody] HeartbeatRequest req)
    {
        if (!IsValidDeviceId(deviceId))
            return BadRequest(ApiError.Of("device.invalid_id", "Device id must be 8-128 chars, alphanumeric + dash."));
        req ??= new HeartbeatRequest();

        var serverTs = DateTimeOffset.UtcNow;
        var previous = _store.RecordHeartbeat(deviceId, req, serverTs);

        var isReconnect = previous is null || (serverTs - previous.LastSeen) > ReconnectGap;
        if (isReconnect)
        {
            var detail = JsonSerializer.Serialize(new
            {
                device_id = deviceId,
                app_version = req.AppVersion,
                mode = req.Mode,
                platform = req.Platform,
                gap_seconds = previous is null ? (double?)null : (serverTs - previous.LastSeen).TotalSeconds,
            });
            await _audit.EmitAsync(
                action: AuditDeviceReconnect,
                actor: User.FindFirstValue(ClaimTypes.Name) ?? "anonymous",
                actorRole: User.FindFirstValue(ClaimTypes.Role) ?? "",
                targetType: "Device",
                targetId: deviceId,
                detail: detail);
        }

        return Ok(new HeartbeatResponse { ServerTimestamp = serverTs });
    }

    // ── helpers ─────────────────────────────────────────────────────────

    private static bool IsValidDeviceId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Length is < 8 or > 128) return false;
        foreach (var ch in id)
        {
            if (!char.IsLetterOrDigit(ch) && ch != '-' && ch != '_') return false;
        }
        return true;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
