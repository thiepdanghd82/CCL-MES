using CCL.MES.Hybrid.Client.Grid;
using Microsoft.Maui.Storage;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// MAUI host impl of <see cref="IGridPreferenceStore"/>. Persists each
/// grid's hidden-set as a single comma-separated string under
/// <c>cclmes.hybrid.grid-cols.{gridKey}.v1</c>. Comma is a safe
/// delimiter because column ids in the grid registries are constrained
/// to lowercase letters + digits + underscores.
///
/// Per Lesson "PostConfigure<IServiceProvider>" carried from P10.3 W4
/// — the store is a singleton; its constructor runs once at host build
/// time. Preferences.Default is process-safe + thread-safe per the
/// MAUI docs.
/// </summary>
public sealed class MauiGridPreferenceStore : IGridPreferenceStore
{
    private const string KeyPrefix = "cclmes.hybrid.grid-cols.";
    private const string KeySuffix = ".v1";

    public IReadOnlySet<string> GetHiddenColumns(string gridKey)
    {
        var raw = Preferences.Default.Get(BuildKey(gridKey), string.Empty);
        if (string.IsNullOrEmpty(raw)) return new HashSet<string>();
        return new HashSet<string>(
            raw.Split(',', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
    }

    public void SetHiddenColumns(string gridKey, IEnumerable<string> hidden)
    {
        var joined = string.Join(',', hidden);
        if (string.IsNullOrEmpty(joined))
            Preferences.Default.Remove(BuildKey(gridKey));
        else
            Preferences.Default.Set(BuildKey(gridKey), joined);
    }

    private static string BuildKey(string gridKey) => KeyPrefix + gridKey + KeySuffix;
}
