namespace CCL.MES.Hybrid.Client.Windows;

/// <summary>
/// One registry entry: everything needed to open a window for a logical
/// <c>key</c> WITHOUT the caller knowing the concrete component type.
/// </summary>
/// <param name="Key">Dedupe + lookup key (a route path in PR1, e.g. <c>"/qms/history"</c>).</param>
/// <param name="ContentType">Component type rendered as the window body.</param>
/// <param name="TitleKey">i18n key for the window title (resolved by the host
/// via <c>ITranslator</c>). NOT the literal title — keeps VI/EN parity.</param>
/// <param name="Icon">Optional taskbar icon token.</param>
/// <param name="RequiredRoles">Roles allowed to open this window, or null for
/// any authenticated user. RBAC-by-omission: the host builds the launcher item
/// only when the role matches; the SERVER still authorises every page/API call
/// (defense in depth — this list is a UI convenience, not the gate).</param>
public sealed record WindowRegistryEntry(
    string Key,
    Type ContentType,
    string TitleKey,
    string? Icon = null,
    string[]? RequiredRoles = null);

/// <summary>
/// Maps a logical <c>key</c> → the window it opens. HOST-POPULATED: entries are
/// registered by the Razor host (P2 UX layer) because the concrete page types
/// live in the Razor project, which references this Client library — NOT the
/// reverse. The Client ships only the shape + an easy-to-extend registration
/// API; adding a window = one <see cref="Register"/> call.
/// </summary>
public interface IWindowRegistry
{
    /// <summary>All registered entries, in registration order.</summary>
    IReadOnlyList<WindowRegistryEntry> Entries { get; }

    /// <summary>Add / replace the entry for <c>entry.Key</c>. Idempotent by key.</summary>
    void Register(WindowRegistryEntry entry);

    /// <summary>Look up an entry by key, or null if not registered.</summary>
    WindowRegistryEntry? Resolve(string key);
}

/// <inheritdoc />
public sealed class WindowRegistry : IWindowRegistry
{
    private readonly List<WindowRegistryEntry> _entries = new();
    private readonly Dictionary<string, int> _indexByKey = new(StringComparer.Ordinal);

    public IReadOnlyList<WindowRegistryEntry> Entries => _entries;

    public void Register(WindowRegistryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(entry.Key);
        ArgumentNullException.ThrowIfNull(entry.ContentType);

        if (_indexByKey.TryGetValue(entry.Key, out var i))
        {
            _entries[i] = entry; // replace-by-key (idempotent registration)
        }
        else
        {
            _indexByKey[entry.Key] = _entries.Count;
            _entries.Add(entry);
        }
    }

    public WindowRegistryEntry? Resolve(string key) =>
        _indexByKey.TryGetValue(key, out var i) ? _entries[i] : null;
}

/// <summary>
/// The PR1 window subset — verified param-free, self-contained read-only grid
/// pages (single <c>@page</c> route, no <c>[Parameter]</c>, no required
/// <c>[CascadingParameter]</c>, no <c>EditForm</c>; DI-only via <c>@inject</c>).
/// The Razor host registers each of these against its concrete page type at
/// startup (the type reference must live host-side per the dependency rule).
/// These are the KEYS + i18n title keys + RBAC roles the host binds to.
/// </summary>
/// <remarks>
/// Deliberately NOT the 34-route surface — that migration is PR2. Extend by
/// appending a constant + one host-side <c>Register</c> call.
/// </remarks>
public static class WindowRegistryKeys
{
    /// <summary>QMS QC History grid — <c>@page "/qms/history"</c>, <c>[Authorize]</c> (any auth).</summary>
    public const string QcHistory = "/qms/history";

    /// <summary>Shop Order History grid — <c>@page "/shop-orders"</c>, <c>[Authorize]</c> (any auth).</summary>
    public const string ShopOrderHistory = "/shop-orders";

    /// <summary>Machine Dashboard grid — <c>@page "/machines"</c>, <c>[Authorize]</c> (any auth).</summary>
    public const string MachineDashboard = "/machines";

    /// <summary>QC Check-Item Library grid — <c>@page "/qc/library"</c>,
    /// <c>[Authorize(Roles = "Admin,Supervisor,Engineer,QC")]</c>.</summary>
    public const string QcLibrary = "/qc/library";

    /// <summary>i18n title keys the host should register alongside each type.
    /// (String constants only — the actual VI/EN strings live in the
    /// TranslationCatalog / SharedResource, added by the UX/i18n agent.)</summary>
    public static class TitleKeys
    {
        public const string QcHistory = "windows.qc_history.title";
        public const string ShopOrderHistory = "windows.shop_order_history.title";
        public const string MachineDashboard = "windows.machine_dashboard.title";
        public const string QcLibrary = "windows.qc_library.title";
    }

    /// <summary>RBAC roles for QcLibrary (mirrors its page <c>[Authorize]</c>).
    /// The other three are any-auth (null roles).</summary>
    public static readonly string[] QcLibraryRoles = { "Admin", "Supervisor", "Engineer", "QC" };
}
