namespace CCL.MES.Hybrid.Client.Grid;

/// <summary>
/// P10.5a — abstraction over the MAUI Preferences key/value store so
/// grid column-visibility state can persist across launches without
/// coupling the grid logic to <c>Microsoft.Maui.Storage.Preferences</c>.
/// Unit tests bind an in-memory implementation
/// (<see cref="InMemoryGridPreferenceStore"/>); the MAUI host wires the
/// real Preferences-backed one.
///
/// Storage key convention: <c>cclmes.hybrid.grid-cols.{gridKey}.v1</c>.
/// Values are a comma-separated list of HIDDEN column ids — the
/// inversion (storing what is hidden, not what is visible) means a
/// future column added to a grid renders visible by default for users
/// who already saved a preference, which matches operator expectation.
/// </summary>
public interface IGridPreferenceStore
{
    /// <summary>Returns the set of HIDDEN column ids for this grid.
    /// Empty set when the user has not saved anything yet.</summary>
    IReadOnlySet<string> GetHiddenColumns(string gridKey);

    /// <summary>Persist the set of HIDDEN column ids. The store
    /// implementation is free to write through to disk asynchronously
    /// — callers should not rely on durability before app close.</summary>
    void SetHiddenColumns(string gridKey, IEnumerable<string> hidden);
}

/// <summary>
/// Process-scoped in-memory store. Default impl for tests + a safe
/// fallback in any host that doesn't ship a real Preferences impl.
/// </summary>
public sealed class InMemoryGridPreferenceStore : IGridPreferenceStore
{
    private readonly Dictionary<string, HashSet<string>> _state = new();
    private readonly object _lock = new();

    public IReadOnlySet<string> GetHiddenColumns(string gridKey)
    {
        lock (_lock)
        {
            return _state.TryGetValue(gridKey, out var set)
                ? new HashSet<string>(set)
                : new HashSet<string>();
        }
    }

    public void SetHiddenColumns(string gridKey, IEnumerable<string> hidden)
    {
        lock (_lock)
        {
            _state[gridKey] = new HashSet<string>(hidden, StringComparer.Ordinal);
        }
    }
}
