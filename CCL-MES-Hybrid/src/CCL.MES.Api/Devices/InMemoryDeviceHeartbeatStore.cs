using System.Collections.Concurrent;
using CCL.MES.Shared.Devices;

namespace CCL.MES.Api.Devices;

/// <summary>
/// Default <see cref="IDeviceHeartbeatStore"/>. Thread-safe via two
/// <see cref="ConcurrentDictionary{TKey, TValue}"/>s — one for last-seen
/// snapshots, one for scan-time queues keyed by device id. We KEEP
/// scan timestamps as a sliding 24-hour window rather than just a
/// monotonic counter so the counter stays bounded (≤ a few thousand per
/// busy station per day) and the GET endpoint reads a fresh count
/// without needing to wait for a janitor pass.
///
/// Memory ceiling: each station's scan-queue holds up to N entries
/// where N is at most one decoded scan per 5 seconds (~17k/day worst
/// case). Each entry is 16 bytes for the timestamp + 16 for the Guid →
/// ~500 KB per station per day, dropped on the 24h pruning. Across
/// 100 stations that's &lt;50 MB — fine for the in-memory tier; durable
/// store is a P10.4 cache concern.
/// </summary>
public sealed class InMemoryDeviceHeartbeatStore : IDeviceHeartbeatStore
{
    private readonly ConcurrentDictionary<string, DeviceHeartbeatSnapshot> _snapshots = new();
    private readonly ConcurrentDictionary<string, List<(Guid Id, DateTimeOffset At)>> _scanWindows = new();
    private readonly object _scanWriteLock = new();

    public DeviceHeartbeatSnapshot? RecordHeartbeat(string deviceId, HeartbeatRequest req, DateTimeOffset serverTimestamp)
    {
        DeviceHeartbeatSnapshot? previous = _snapshots.TryGetValue(deviceId, out var p) ? p : null;
        var fresh = new DeviceHeartbeatSnapshot(
            DeviceId: deviceId,
            LastSeen: serverTimestamp,
            LastAppVersion: req.AppVersion,
            LastMode: req.Mode,
            LastPlatform: req.Platform,
            ScanCountLast24h: CountScansInWindow(deviceId, serverTimestamp));
        _snapshots[deviceId] = fresh;
        return previous;
    }

    public void RecordScan(string deviceId, Guid scanId, DateTimeOffset serverTimestamp)
    {
        // Idempotency guard — if the same scanId is replayed (network retry)
        // do not double-count. We pay an O(N) scan over the window here
        // because windows are bounded by 24h, far cheaper than backing each
        // station with a hash set.
        lock (_scanWriteLock)
        {
            var window = _scanWindows.GetOrAdd(deviceId, _ => new List<(Guid, DateTimeOffset)>());
            if (window.Any(e => e.Id == scanId)) return;
            window.Add((scanId, serverTimestamp));
            PruneOldEntries(window, serverTimestamp);
        }

        // Refresh the snapshot's counter if a snapshot exists, so GET reflects
        // the new scan immediately without needing to wait for next heartbeat.
        if (_snapshots.TryGetValue(deviceId, out var snap))
        {
            _snapshots[deviceId] = snap with { ScanCountLast24h = CountScansInWindow(deviceId, serverTimestamp) };
        }
    }

    public DeviceHeartbeatSnapshot? Get(string deviceId) =>
        _snapshots.TryGetValue(deviceId, out var snap) ? snap : null;

    private int CountScansInWindow(string deviceId, DateTimeOffset now)
    {
        if (!_scanWindows.TryGetValue(deviceId, out var window)) return 0;
        var threshold = now - TimeSpan.FromHours(24);
        // Read-only count — no mutation under lock — safe because the list
        // is only appended to under _scanWriteLock and we tolerate seeing
        // an entry mid-append (over-count by 1 worst case).
        return window.Count(e => e.At >= threshold);
    }

    private static void PruneOldEntries(List<(Guid Id, DateTimeOffset At)> window, DateTimeOffset now)
    {
        var threshold = now - TimeSpan.FromHours(24);
        window.RemoveAll(e => e.At < threshold);
    }
}
