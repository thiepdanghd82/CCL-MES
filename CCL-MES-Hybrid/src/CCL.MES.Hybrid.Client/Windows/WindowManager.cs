namespace CCL.MES.Hybrid.Client.Windows;

/// <summary>
/// Session-only registry of open floating windows. Owns membership, focus,
/// z-order and min/max state; raises <see cref="Changed"/> on every mutation
/// so the host re-renders the workspace + taskbar. Registered as a singleton
/// (survives page navigation within the session).
/// </summary>
/// <remarks>
/// Deliberately NOT persisted (no localStorage / disk) — the list is empty on
/// every fresh session. Distinct from <see cref="IFloatingWindowStore"/>,
/// which remembers per-key RECT geometry only and knows nothing about the
/// open-list, focus or z-order.
/// </remarks>
public interface IWindowManager
{
    /// <summary>Open windows in STABLE OPEN ORDER (oldest first). The host
    /// uses this order for the <c>@key</c> loop so component instances are not
    /// destroyed/recreated when focus changes. Z-index layering is driven by
    /// <see cref="OpenWindow.ZOrder"/>, NOT by this order.</summary>
    IReadOnlyList<OpenWindow> Windows { get; }

    /// <summary>The single active (focused, non-minimized) window, or null
    /// when the workspace is empty or every window is minimized.</summary>
    OpenWindow? Active { get; }

    /// <summary>Soft cap on concurrent windows. Opening when full is BLOCKED
    /// (returns null) — the manager never auto-closes an existing window.</summary>
    int SoftCap { get; }

    /// <summary>
    /// Open a window for <paramref name="key"/>, or focus + return the
    /// existing one if that key is already open (dedupe). Returns null when
    /// the workspace is at <see cref="SoftCap"/> (open blocked; nothing
    /// mutated). A newly opened window becomes active with the top z-order.
    /// </summary>
    OpenWindow? Open(string key, string title, string? icon, Type contentType,
        IReadOnlyDictionary<string, object>? parameters = null);

    /// <summary>Bring a window to the top: raise z-order, restore from
    /// minimized to normal, and make it the sole active window.</summary>
    void Focus(string id);

    /// <summary>Minimize a window. If it was active, active moves to the
    /// remaining non-minimized window with the highest z-order (or null).</summary>
    void Minimize(string id);

    /// <summary>Maximize a window and focus it.</summary>
    void Maximize(string id);

    /// <summary>Restore a window to <see cref="WindowState.Normal"/> from
    /// minimized or maximized (does not change focus on its own).</summary>
    void Restore(string id);

    /// <summary>Close (remove) a window. If it was active, active moves to the
    /// remaining non-minimized window with the highest z-order (or null).</summary>
    void Close(string id);

    /// <summary>Raised after any mutation so the host can re-render.</summary>
    event Action? Changed;
}

/// <inheritdoc />
public sealed class WindowManager : IWindowManager
{
    private const int DefaultSoftCap = 8;

    // Stable OPEN order — never sorted. Host relies on this for @key stability.
    private readonly List<OpenWindow> _windows = new();
    private int _zCounter;

    public IReadOnlyList<OpenWindow> Windows => _windows;

    public OpenWindow? Active =>
        _windows.FirstOrDefault(w => w.IsActive && w.State != WindowState.Minimized);

    public int SoftCap { get; } = DefaultSoftCap;

    public event Action? Changed;

    public OpenWindow? Open(string key, string title, string? icon, Type contentType,
        IReadOnlyDictionary<string, object>? parameters = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(contentType);

        // Dedupe: an already-open key focuses the existing window (Count unchanged).
        var existing = _windows.FirstOrDefault(w => w.Key == key);
        if (existing is not null)
        {
            FocusInternal(existing);
            Changed?.Invoke();
            return existing;
        }

        // Soft cap: blocked when full. Never auto-close; return null, no mutation.
        if (_windows.Count >= SoftCap)
        {
            return null;
        }

        var window = new OpenWindow
        {
            Id = Guid.NewGuid().ToString("N"),
            Key = key,
            Title = title,
            Icon = icon,
            ContentType = contentType,
            Parameters = parameters,
            State = WindowState.Normal,
        };
        _windows.Add(window);
        FocusInternal(window);
        Changed?.Invoke();
        return window;
    }

    public void Focus(string id)
    {
        var w = Find(id);
        if (w is null) return;
        FocusInternal(w);
        Changed?.Invoke();
    }

    public void Minimize(string id)
    {
        var w = Find(id);
        if (w is null || w.State == WindowState.Minimized) return;

        var wasActive = w.IsActive;
        w.State = WindowState.Minimized;
        w.IsActive = false;
        if (wasActive)
        {
            PromoteNextActive();
        }
        Changed?.Invoke();
    }

    public void Maximize(string id)
    {
        var w = Find(id);
        if (w is null) return;
        w.State = WindowState.Maximized;
        FocusInternal(w);
        Changed?.Invoke();
    }

    public void Restore(string id)
    {
        var w = Find(id);
        if (w is null || w.State == WindowState.Normal) return;
        w.State = WindowState.Normal;
        Changed?.Invoke();
    }

    public void Close(string id)
    {
        var w = Find(id);
        if (w is null) return;

        var wasActive = w.IsActive;
        _windows.Remove(w);
        if (wasActive)
        {
            PromoteNextActive();
        }
        Changed?.Invoke();
    }

    private OpenWindow? Find(string id) => _windows.FirstOrDefault(w => w.Id == id);

    // Raise z, un-minimize, make sole active. No Changed here — callers fire once.
    private void FocusInternal(OpenWindow w)
    {
        if (w.State == WindowState.Minimized)
        {
            w.State = WindowState.Normal;
        }
        w.ZOrder = ++_zCounter;
        foreach (var other in _windows)
        {
            other.IsActive = ReferenceEquals(other, w);
        }
    }

    // Active fell away (minimized/closed): promote the top non-minimized window.
    private void PromoteNextActive()
    {
        var next = _windows
            .Where(w => w.State != WindowState.Minimized)
            .OrderByDescending(w => w.ZOrder)
            .FirstOrDefault();
        foreach (var w in _windows)
        {
            w.IsActive = next is not null && ReferenceEquals(w, next);
        }
    }
}
