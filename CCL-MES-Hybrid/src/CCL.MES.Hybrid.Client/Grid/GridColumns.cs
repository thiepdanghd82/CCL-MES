namespace CCL.MES.Hybrid.Client.Grid;

/// <summary>
/// P10.5a — declarative column registry per grid. Pages declare an
/// ordered list of <see cref="ColumnDef"/>s and bind to a
/// <see cref="GridColumns"/> instance to query "is this column visible
/// right now". Toggling persists via <see cref="IGridPreferenceStore"/>
/// so reopening the grid restores the operator's last layout.
///
/// Per Q12 (i18n inline VN): <see cref="ColumnDef.Label"/> carries the
/// Vietnamese label inline; an English alias may follow in a follow-up
/// PR once the resx infrastructure lands.
///
/// Encoding (compatible with <see cref="IGridPreferenceStore"/>'s
/// hidden-set contract):
///   - Plain column id (e.g. <c>"area"</c>) = "hide this column".
///   - Sentinel-prefixed id (e.g. <c>"+notes"</c>) = "show this column
///     even if <see cref="ColumnDef.DefaultVisible"/> is false".
/// This way the store stays a single string set and the encoding is
/// idempotent — two different operators with different preferences on
/// the same grid don't conflict.
/// </summary>
public sealed record ColumnDef(
    string Id,
    string Label,
    bool DefaultVisible = true);

public sealed class GridColumns
{
    private const string ShowOverridePrefix = "+";

    private readonly IGridPreferenceStore _store;
    private readonly string _gridKey;
    private readonly IReadOnlyList<ColumnDef> _columns;
    private HashSet<string> _state;

    public GridColumns(string gridKey, IEnumerable<ColumnDef> columns, IGridPreferenceStore store)
    {
        _store = store;
        _gridKey = gridKey;
        _columns = columns.ToList();
        _state = new HashSet<string>(_store.GetHiddenColumns(gridKey), StringComparer.Ordinal);
    }

    /// <summary>All declared columns in declaration order.</summary>
    public IReadOnlyList<ColumnDef> All => _columns;

    /// <summary>Columns currently rendered, declaration order preserved.</summary>
    public IReadOnlyList<ColumnDef> Visible =>
        _columns.Where(c => IsVisible(c.Id)).ToList();

    /// <summary>True when the column renders right now. Operator
    /// overrides win over the column's own default; absent any
    /// override the <see cref="ColumnDef.DefaultVisible"/> wins.</summary>
    public bool IsVisible(string columnId)
    {
        var col = _columns.FirstOrDefault(c => c.Id == columnId);
        if (col is null) return false;

        // Explicit "show me" override flips a default-hidden column on.
        if (_state.Contains(ShowOverridePrefix + columnId)) return true;
        // Explicit "hide me" override flips a default-visible column off.
        if (_state.Contains(columnId)) return false;
        // No override — honor the declared default.
        return col.DefaultVisible;
    }

    /// <summary>
    /// Flip the column's visibility. Persists the new state immediately.
    /// </summary>
    public void Toggle(string columnId)
    {
        var col = _columns.FirstOrDefault(c => c.Id == columnId);
        if (col is null) return;

        var nowVisible = IsVisible(columnId);
        // Clear any existing override for this column id first — keeps the
        // state set free of stale conflicting entries.
        _state.Remove(columnId);
        _state.Remove(ShowOverridePrefix + columnId);

        if (nowVisible)
        {
            // We want to hide it next render.
            if (col.DefaultVisible)
            {
                // Default-visible column needs an explicit "hide me" marker.
                _state.Add(columnId);
            }
            // Default-hidden column was visible via "+" override; removing
            // that override (done above) is sufficient.
        }
        else
        {
            // We want to show it next render.
            if (!col.DefaultVisible)
            {
                // Default-hidden column needs an explicit "show me" marker.
                _state.Add(ShowOverridePrefix + columnId);
            }
            // Default-visible column was hidden via plain marker; removing
            // that marker (done above) is sufficient.
        }

        _store.SetHiddenColumns(_gridKey, _state);
    }

    /// <summary>Drop every override — revert to the declared defaults.</summary>
    public void ResetToDefaults()
    {
        _state.Clear();
        _store.SetHiddenColumns(_gridKey, _state);
    }

    /// <summary>Re-read the underlying store — useful when a sibling
    /// surface (e.g. a global "reset all grids" admin action) mutates
    /// preferences. Not currently wired but cheap to expose.</summary>
    public void Reload()
    {
        _state = new HashSet<string>(_store.GetHiddenColumns(_gridKey), StringComparer.Ordinal);
    }
}
