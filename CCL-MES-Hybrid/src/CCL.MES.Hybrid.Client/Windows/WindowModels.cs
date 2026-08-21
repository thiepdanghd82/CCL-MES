namespace CCL.MES.Hybrid.Client.Windows;

/// <summary>
/// Visual state of one managed window. <see cref="Minimized"/> is the state
/// the host uses to STOP polling in the window body (see
/// <see cref="WindowContext"/>). <see cref="Maximized"/> fills the workspace;
/// <see cref="Normal"/> is the free-floating rect the <see cref="IFloatingWindowStore"/>
/// remembers.
/// </summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
}

/// <summary>
/// One live window tracked by <see cref="IWindowManager"/> for the SESSION
/// (never persisted — deliberately session-only per Henry). A window is a
/// content component (<see cref="ContentType"/>) rendered inside the shared
/// <c>FloatingWindow</c> chrome. The manager owns list membership, focus,
/// z-order and min/max state; the host (P2-PR1 UX) renders each entry and
/// cascades <see cref="WindowContext"/> down so the body can pause polling
/// when <see cref="State"/> is <see cref="WindowState.Minimized"/>.
/// </summary>
public sealed class OpenWindow
{
    /// <summary>Stable per-window id (a GUID). The host uses it as the
    /// <c>FloatingWindow.WindowId</c> AND as the Blazor <c>@key</c> so the
    /// component instance survives re-renders. Never reused.</summary>
    public required string Id { get; init; }

    /// <summary>Dedupe key — a logical identity for "the same thing".
    /// Typically a route path (e.g. <c>"/qms/history"</c>) or a domain
    /// selector (e.g. <c>"trace:WO-26-3685"</c>). Opening with a key that is
    /// already open focuses the existing window instead of duplicating it.</summary>
    public required string Key { get; init; }

    /// <summary>Title shown in the window titlebar + taskbar. Mutable so a
    /// body can rename itself (e.g. once data loads).</summary>
    public required string Title { get; set; }

    /// <summary>Optional icon token (glyph name / emoji) for the taskbar.</summary>
    public string? Icon { get; init; }

    /// <summary>The component type rendered as the window body. The host uses
    /// a <c>DynamicComponent</c> keyed on this. Stored as <see cref="Type"/>
    /// so the Client library stays free of any Blazor component reference
    /// (the page types live in the Razor project, which references Client —
    /// NOT the reverse).</summary>
    public required Type ContentType { get; init; }

    /// <summary>Optional parameters forwarded to the body component. Null for
    /// the param-free registry pages shipped in PR1.</summary>
    public IReadOnlyDictionary<string, object>? Parameters { get; init; }

    /// <summary>Current visual state. Mutated by the manager only.</summary>
    public WindowState State { get; set; } = WindowState.Normal;

    /// <summary>Higher = drawn on top. The host binds this to CSS z-index.
    /// Managed entirely by the manager; do NOT sort <see cref="IWindowManager.Windows"/>
    /// by it — that list keeps stable OPEN order for @key.</summary>
    public int ZOrder { get; set; }

    /// <summary>True for exactly one non-minimized window at a time (or none
    /// when the workspace is empty / all minimized). Drives the "focused"
    /// chrome accent + which body is considered foreground.</summary>
    public bool IsActive { get; set; }
}

/// <summary>
/// Cascading value the HOST supplies per window so a window body can pause
/// expensive work while its window is minimized.
/// </summary>
/// <remarks>
/// The <see cref="IWindowManager"/> does NOT cascade this itself — the host
/// (P2-PR1 UX layer) wraps each rendered window with
/// <c>&lt;CascadingValue Value="new WindowContext { IsActive = w.State != WindowState.Minimized }"&gt;</c>.
/// A body that polls should opt in with
/// <c>[CascadingParameter] WindowContext? Win</c> and guard its loop with
/// <c>if (Win?.IsActive != false) { ...poll... }</c> — i.e. poll when active
/// OR when no context is supplied (standalone / non-windowed hosting), and
/// SKIP only when explicitly minimized. No window body is modified in this PR;
/// this type is the contract the UX agent wires to.
/// </remarks>
public sealed class WindowContext
{
    /// <summary>False only while the owning window is minimized. A polling
    /// body should treat <c>null</c> context (no cascade) as active.</summary>
    public bool IsActive { get; init; }
}
