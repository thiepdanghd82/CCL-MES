namespace CCL.MES.Hybrid.Client.Windows;

/// <summary>
/// Position + size of a floating showcard. Mirrors the JS-side rect object
/// (see <c>floating-window.js</c>): the <c>cclMesFloat</c> module reports the
/// final geometry on pointer-up / resize-end / maximize, and the component
/// serialises it into this record via <c>OnRectChanged</c>. Property names are
/// lower-cased in JSON to match the JS payload (STJ web defaults are
/// case-insensitive, but keeping the names aligned avoids surprises).
/// </summary>
public sealed record WindowRect
{
    public double X { get; init; }
    public double Y { get; init; }
    public double W { get; init; }
    public double H { get; init; }
    public bool Maximized { get; init; }
}

/// <summary>
/// Remembers each showcard's last rect FOR THE SESSION so re-opening the same
/// Work Order restores where the operator left it. Keyed by WoNo. A singleton
/// (survives page navigation) but NOT persisted to disk — deliberately
/// session-scoped, with <see cref="Clear"/> as the "reset to centre" escape
/// hatch the toolbar exposes.
/// </summary>
public interface IFloatingWindowStore
{
    WindowRect? Get(string key);
    void Save(string key, WindowRect rect);
    void Remove(string key);
    void Clear();
    int Count { get; }
}

/// <inheritdoc />
public sealed class InMemoryFloatingWindowStore : IFloatingWindowStore
{
    private readonly Dictionary<string, WindowRect> _rects = new(StringComparer.OrdinalIgnoreCase);

    public WindowRect? Get(string key) => _rects.TryGetValue(key, out var r) ? r : null;
    public void Save(string key, WindowRect rect) => _rects[key] = rect;
    public void Remove(string key) => _rects.Remove(key);
    public void Clear() => _rects.Clear();
    public int Count => _rects.Count;
}
