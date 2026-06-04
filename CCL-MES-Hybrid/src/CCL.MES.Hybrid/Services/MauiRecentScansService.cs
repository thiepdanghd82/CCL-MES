using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Shared.RecentScans;
using Microsoft.Maui.Storage;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// MAUI host impl of <see cref="IRecentScansService"/>. Persists the
/// entire ring as a JSON string in MAUI Preferences under key
/// <c>cclmes.hybrid.recent-scans.v1</c>. Mirrors the
/// <see cref="MauiGridPreferenceStore"/> singleton pattern from
/// P10.5a so the same DI / lifecycle assumptions hold:
/// <c>Preferences.Default</c> is documented thread-safe + process-safe
/// by MAUI, so a single in-process lock guards the read-modify-write
/// race that the WebView dispatcher can otherwise trip when two
/// scan→record flows resolve on different threads near-simultaneously.
///
/// JSON shape is owned by <see cref="RecentScansSerializer"/> in the
/// client library so the in-memory + persisted impls cannot drift.
/// </summary>
public sealed class MauiRecentScansService : IRecentScansService
{
    private const string PreferenceKey = "cclmes.hybrid.recent-scans.v1";
    private readonly object _lock = new();

    public event EventHandler? Changed;

    public IReadOnlyList<RecentScanEntry> GetRecent(int maxCount = IRecentScansService.DefaultDisplayCount)
    {
        if (maxCount <= 0) return Array.Empty<RecentScanEntry>();
        lock (_lock)
        {
            var snapshot = LoadSnapshot();
            return snapshot.Take(maxCount).ToList();
        }
    }

    public void Record(RecentScanEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.Code)) return;

        lock (_lock)
        {
            var list = new LinkedList<RecentScanEntry>(LoadSnapshot());
            InMemoryRecentScansService.ApplyRecord(list, entry);
            SaveSnapshot(list);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        bool mutated;
        lock (_lock)
        {
            mutated = !string.IsNullOrEmpty(Preferences.Default.Get(PreferenceKey, string.Empty));
            Preferences.Default.Remove(PreferenceKey);
        }
        if (mutated) Changed?.Invoke(this, EventArgs.Empty);
    }

    private static List<RecentScanEntry> LoadSnapshot()
    {
        var raw = Preferences.Default.Get(PreferenceKey, string.Empty);
        return RecentScansSerializer.Deserialize(raw);
    }

    private static void SaveSnapshot(LinkedList<RecentScanEntry> list)
    {
        var raw = RecentScansSerializer.Serialize(list);
        Preferences.Default.Set(PreferenceKey, raw);
    }
}
