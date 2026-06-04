using System.Text.Json;
using CCL.MES.Shared.RecentScans;

namespace CCL.MES.Hybrid.Client.RecentScans;

/// <summary>
/// P10.6f — operator-facing "Quét gần đây" sidebar widget service.
///
/// Mirrors SpecHub §11 (<c>RECENT_SCANS</c> localStorage) ported to
/// MAUI Preferences. The widget is client-only — no server endpoint —
/// because:
///   1. The forensic trail is already covered by W4
///      <c>POST /api/v2/devices/{id}/scan-log</c> (online-only). Recent
///      Scans is the per-device UX shortcut, not the audit source.
///   2. Operators want the widget to render even when the LAN is dead.
///   3. The data is intentionally per-device + per-OS-account — moving
///      to another station resets the list.
///
/// Storage key: <c>cclmes.hybrid.recent-scans.v1</c>. Encoded as a
/// JSON array of <see cref="RecentScanEntry"/>. Cap at
/// <see cref="MaxCapacity"/> entries — older rows drop on
/// <see cref="Record"/>. The widget displays the newest
/// <see cref="DefaultDisplayCount"/> (5 per SpecHub spec).
///
/// Concurrency: <see cref="Record"/> dedupes by <c>Code+Context</c> so
/// re-scanning the same WO collapses to a single row whose
/// <see cref="RecentScanEntry.ScannedAt"/> is the latest touch.
/// Implementations MUST be thread-safe; the MAUI host hits Preferences
/// from any thread the WebView's dispatcher resumes on.
/// </summary>
public interface IRecentScansService
{
    /// <summary>Widget default display count — newest first.</summary>
    public const int DefaultDisplayCount = 5;

    /// <summary>Maximum entries kept in the store. Older entries
    /// drop on the next <see cref="Record"/> call.</summary>
    public const int MaxCapacity = 25;

    /// <summary>Returns the newest <paramref name="maxCount"/>
    /// entries, newest first. Empty list when the operator hasn't
    /// scanned anything yet.</summary>
    IReadOnlyList<RecentScanEntry> GetRecent(int maxCount = DefaultDisplayCount);

    /// <summary>Push a new scan to the front. Dedupes by
    /// <see cref="RecentScanEntry.Code"/> + <see cref="RecentScanEntry.Context"/>;
    /// the existing row is removed and the new one inserted at the
    /// head. Triggers <see cref="Changed"/> when the store
    /// actually mutated (idempotent re-records skip the event).</summary>
    void Record(RecentScanEntry entry);

    /// <summary>Wipe the entire list. Operator-visible via the
    /// widget header "Xoá lịch sử" affordance. Triggers
    /// <see cref="Changed"/> when the store was non-empty.</summary>
    void Clear();

    /// <summary>Fires after a mutation lands. The widget subscribes
    /// to refresh its render synchronously on the UI thread.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// Default in-memory implementation. Used by tests, by non-MAUI hosts,
/// and as a safety net before <see cref="MauiProgram"/>'s DI Replace
/// step lands the Preferences-backed impl. Mirrors the
/// <see cref="InMemoryGridPreferenceStore"/> pattern from P10.5a.
/// </summary>
public sealed class InMemoryRecentScansService : IRecentScansService
{
    private readonly object _lock = new();
    private readonly LinkedList<RecentScanEntry> _entries = new();

    public event EventHandler? Changed;

    public IReadOnlyList<RecentScanEntry> GetRecent(int maxCount = IRecentScansService.DefaultDisplayCount)
    {
        if (maxCount <= 0) return Array.Empty<RecentScanEntry>();
        lock (_lock)
        {
            return _entries.Take(maxCount).ToList();
        }
    }

    public void Record(RecentScanEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Code)) return;

        bool mutated;
        lock (_lock)
        {
            mutated = ApplyRecord(_entries, entry);
        }
        if (mutated) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        bool mutated;
        lock (_lock)
        {
            mutated = _entries.Count > 0;
            _entries.Clear();
        }
        if (mutated) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shared helper — the Preferences-backed impl loads a
    /// snapshot into a <see cref="LinkedList{T}"/>, calls this, and
    /// writes the snapshot back. Keeps dedupe + cap logic in one
    /// place so the in-memory + persisted impls can't drift. Public
    /// because the MAUI host's <c>MauiRecentScansService</c> lives in
    /// a separate assembly and consumes it.</summary>
    public static bool ApplyRecord(LinkedList<RecentScanEntry> list, RecentScanEntry entry)
    {
        // Dedupe by (Code, Context). Both are operator-meaningful axes —
        // the same WO code scanned under "wo-accept" vs a hypothetical
        // "qc-capture" context are different operations.
        LinkedListNode<RecentScanEntry>? existing = null;
        for (var node = list.First; node is not null; node = node.Next)
        {
            if (string.Equals(node.Value.Code, entry.Code, StringComparison.Ordinal)
                && string.Equals(node.Value.Context, entry.Context, StringComparison.Ordinal))
            {
                existing = node;
                break;
            }
        }
        if (existing is not null) list.Remove(existing);
        list.AddFirst(entry);
        while (list.Count > IRecentScansService.MaxCapacity) list.RemoveLast();
        return true;
    }
}

/// <summary>
/// Pure (de)serialiser the MAUI host wraps around
/// <c>Preferences.Default</c>. Lives in the client library so the
/// in-memory impl + the Preferences impl can't drift on the JSON
/// shape. Defensive on malformed input — a corrupted Preferences
/// string returns an empty list instead of throwing on widget
/// render, so a previous-OS-write doesn't brick the sidebar.
/// </summary>
public static class RecentScansSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // ScannedAt is DateTime UTC — the default ISO-8601 round-trip
        // is fine; no custom converter needed.
    };

    public static string Serialize(IEnumerable<RecentScanEntry> entries)
        => JsonSerializer.Serialize(entries.ToArray(), Options);

    public static List<RecentScanEntry> Deserialize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<RecentScanEntry>();
        try
        {
            var arr = JsonSerializer.Deserialize<RecentScanEntry[]>(raw, Options);
            return arr is null ? new List<RecentScanEntry>() : arr.ToList();
        }
        catch (JsonException)
        {
            // Corrupted persisted blob — wipe-on-read is safer than
            // a render-time crash. Operator just loses history.
            return new List<RecentScanEntry>();
        }
    }
}
