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
/// <param name="Parameters">Optional STATIC parameter set handed to the window's
/// component (via DynamicComponent) every time this key is opened — e.g. the
/// stage lock <c>{ ["Mode"] = "fqc" }</c> for the FQC queue window. Null for a
/// param-free page. Windows whose params are DYNAMIC (e.g. a per-id spec detail)
/// carry no static set here and are opened by the caller with the id-specific
/// dictionary + a per-id key instead.</param>
public sealed record WindowRegistryEntry(
    string Key,
    Type ContentType,
    string TitleKey,
    string? Icon = null,
    string[]? RequiredRoles = null,
    IReadOnlyDictionary<string, object>? Parameters = null);

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

    // ── P2-PR2 subset ──────────────────────────────────────────────────────
    // 9 more param-free, self-contained pages verified window-safe (single
    // @page route, no [Parameter] beyond ambient CascadingAuthenticationState,
    // no self-hosted FloatingWindow, no URL-mode dispatch). Routes that carry a
    // param ({id}), self-host a FloatingWindow (IQC materials showcards,
    // /quality/traceability), share ONE component across two URL-modes
    // (/qms + /qms/fqc), or are a multi-route hub (/settings) are DEFERRED to a
    // follow-up PR — see the p2-pr2 classification note.
    //
    // W5 (this migration) — /workorders NOW joins the window subset. It looked
    // heavily stateful (MesPhase dispatch + AdvanceOrchestrator + 8 child
    // dashboards), but on audit it is window-SAFE: param-free ([Authorize]
    // any-auth, no [Parameter]), it does NOT self-host a FloatingWindow
    // (WorkOrders.razor renders no <FloatingWindow>/role="dialog" — the WM host
    // wraps it via DynamicComponent), it drives no @Body/route dispatch, and its
    // dispatch state lives entirely inside the component (the 8 child dashboards
    // are keep-alive children of the ONE window instance, so a focus / route
    // change never tears down an in-flight scan or advance). The L21
    // OnPhaseChanged re-fetch is internal to the component, not a shell nav.
    //
    // Home ("/") is DELIBERATELY NOT a window key: it is the default full-page
    // landing (Home dashboard renders in @Body). Making it a window key would
    // flip "/" to a window-route and show the empty WorkspaceHome on app open.

    /// <summary>Work Orders scan→advance hub — <c>@page "/workorders"</c>,
    /// <c>[Authorize]</c> (any auth). Param-free page whose 8 child dashboards
    /// (Prepress/Setting/Running/IPQC/QA/FQC/OQC/Legs) are keep-alive children of
    /// the single window instance; no self-hosted FloatingWindow (WM host wraps).</summary>
    public const string WorkOrders = "/workorders";

    /// <summary>NPI Structure (BOM) grid — <c>@page "/npi/structure"</c>,
    /// <c>[Authorize(Roles = "Admin,Supervisor,Engineer,QC")]</c>.</summary>
    public const string NpiStructure = "/npi/structure";

    /// <summary>NPI Routing grid — <c>@page "/npi/routine"</c>, same roles as NPI.</summary>
    public const string NpiRoutine = "/npi/routine";

    /// <summary>NPI Raw Materials grid — <c>@page "/npi/rawmaterials"</c>, same roles.</summary>
    public const string NpiRawMaterials = "/npi/rawmaterials";

    /// <summary>NPI Work Centers grid — <c>@page "/npi/workcenters"</c>, same roles.</summary>
    public const string NpiWorkCenters = "/npi/workcenters";

    /// <summary>Semi-finished store — <c>@page "/warehouse/semi-products"</c>, any auth.</summary>
    public const string SemiProducts = "/warehouse/semi-products";

    /// <summary>QMS Overview dashboard — <c>@page "/qms/dashboard"</c>, any auth.</summary>
    public const string QmsDashboard = "/qms/dashboard";

    /// <summary>IPQC module — <c>@page "/qms/ipqc"</c>, any auth.</summary>
    public const string QmsIpqc = "/qms/ipqc";

    /// <summary>OQC module — <c>@page "/qms/oqc"</c>, any auth.</summary>
    public const string QmsOqc = "/qms/oqc";

    /// <summary>iCRA module — <c>@page "/qms/icra"</c>, any auth.</summary>
    public const string QmsIcra = "/qms/icra";

    // ── P2-PR3 subset — route-param tabs ───────────────────────────────────
    // Two shapes of param-carrying window land here:
    //  1. STATIC-param: one component (QmsQueue) shared across two URL-modes.
    //     The hub /qms and the stage-locked /qms/fqc are two DISTINCT window
    //     keys pointing at the SAME type; only the FQC key carries a static
    //     { ["Mode"] = "fqc" } param so the window locks to the FQC stage
    //     without a per-window URL to read.
    //  2. DYNAMIC-param: the Spec list (/npi/specs) is a plain param-free grid
    //     window, but a row-open spawns a PER-ID spec-detail window keyed
    //     "spec:{id}" with { ["RevisionId"] = id }. The id-specific key + param
    //     are built by the caller (Specs row-open + the WorkspaceRouter
    //     deep-link parse), NOT registered as a fixed entry — one registry row
    //     can't enumerate every spec id.

    /// <summary>QMS Inspection Queue hub — <c>@page "/qms"</c> (3-tab). Distinct
    /// key from the FQC stage window so both can be open at once (dedupe by key).</summary>
    public const string QmsQueueHub = "/qms";

    /// <summary>QMS FQC stage queue — <c>@page "/qms/fqc"</c>. Same component as
    /// the hub, opened with a static <c>{ ["Mode"] = "fqc" }</c> stage lock.</summary>
    public const string QmsQueueFqc = "/qms/fqc";

    /// <summary>Engineer Spec list — <c>@page "/npi/specs"</c>,
    /// <c>[Authorize(Roles = "Admin,Supervisor,Engineer")]</c>. Param-free grid
    /// window; row-open spawns a per-id <see cref="SpecDetailKeyPrefix"/> window.</summary>
    public const string Specs = "/npi/specs";

    /// <summary>Route prefix for the spec-detail param route
    /// (<c>/npi/specs/{RevisionId:long}</c>). Deep-link parsing strips this to
    /// read the id; the per-id window key is <see cref="SpecDetailKeyPrefix"/>.</summary>
    public const string SpecDetailRoutePrefix = "/npi/specs/";

    /// <summary>Per-id window-key prefix for an open spec detail
    /// (<c>spec:{RevisionId}</c>). One window per spec id → dedupe re-focuses the
    /// same spec instead of stacking duplicates.</summary>
    public const string SpecDetailKeyPrefix = "spec:";

    // ── P2 showcard-migration — Traceability (list window + per-WO detail) ──
    // Same shape as the Spec list/detail pair: a param-free grid window whose
    // rows open a per-WoNo detail window. The detail body is TraceabilityDetail
    // (TraceabilityDetailDialog with Chrome=false) so the WM host owns the
    // FloatingWindow — no double-wrap. Detail windows are non-route (opened only
    // from a row dblclick), so they carry no fixed registry entry + no deep-link.

    /// <summary>Quality Traceability list — <c>@page "/quality/traceability"</c>,
    /// <c>[Authorize(Roles = "Admin,Supervisor,QC")]</c>. Param-free grid window;
    /// row-dblclick spawns a per-WO <see cref="TraceDetailKeyPrefix"/> window.</summary>
    public const string Traceability = "/quality/traceability";

    /// <summary>Per-WO window-key prefix for an open traceability detail
    /// (<c>trace:{WoNo}</c>). One window per WO → dedupe re-focuses the same WO
    /// instead of stacking duplicates (mirrors <see cref="SpecDetailKeyPrefix"/>).</summary>
    public const string TraceDetailKeyPrefix = "trace:";

    // ── W5 showcard-migration — IQC Materials inspection windows ────────────
    // The IQC module (/qms/iqc) is NOT itself a registered window (it is a
    // full-page host with 3 sub-tabs). Its Materials inspection SHOWCARDS,
    // however, are now real WM windows (mirror the Traceability detail pattern):
    // opened by MaterialsInspectionWindow (MaterialsInspectionForm with
    // Chrome=false) so the WM host owns the FloatingWindow — no double-wrap.
    // Two key shapes:
    //   • ticket:{ReceiptNo} — a saved ticket → dedupe re-focuses the same one.
    //   • iqc-new:{Guid}     — a brand-new draft → unique key each time so the
    //     operator can open several independent create windows in parallel.

    /// <summary>Per-ReceiptNo window-key prefix for an open (saved) IQC ticket
    /// inspection (<c>ticket:{ReceiptNo}</c>). One window per ticket → dedupe
    /// re-focuses the same ticket instead of stacking duplicates.</summary>
    public const string IqcTicketKeyPrefix = "ticket:";

    /// <summary>Window-key prefix for a brand-new IQC inspection draft
    /// (<c>iqc-new:{Guid}</c>). A fresh Guid per open → NEVER dedupes, so each
    /// "New Ticket → Materials" tap opens an independent create window.</summary>
    public const string IqcNewKeyPrefix = "iqc-new:";

    /// <summary>i18n title keys the host should register alongside each type.
    /// (String constants only — the actual VI/EN strings live in the
    /// TranslationCatalog / SharedResource, added by the UX/i18n agent.)</summary>
    public static class TitleKeys
    {
        public const string QcHistory = "windows.qc_history.title";
        public const string ShopOrderHistory = "windows.shop_order_history.title";
        public const string MachineDashboard = "windows.machine_dashboard.title";
        public const string QcLibrary = "windows.qc_library.title";

        // P2-PR2 subset (Home excluded — "/" is the full-page landing, not a window).
        public const string NpiStructure = "windows.npi_structure.title";
        public const string NpiRoutine = "windows.npi_routine.title";
        public const string NpiRawMaterials = "windows.npi_rawmaterials.title";
        public const string NpiWorkCenters = "windows.npi_workcenters.title";
        public const string SemiProducts = "windows.semi_products.title";
        public const string QmsDashboard = "windows.qms_dashboard.title";
        public const string QmsIpqc = "windows.qms_ipqc.title";
        public const string QmsOqc = "windows.qms_oqc.title";
        public const string QmsIcra = "windows.qms_icra.title";

        // W5 — Work Orders scan→advance hub (any-auth page → window).
        public const string WorkOrders = "windows.workorders.title";

        // P2-PR3 route-param tabs.
        public const string QmsQueueHub = "windows.qms_queue.title";
        public const string QmsQueueFqc = "windows.qms_queue_fqc.title";
        public const string Specs = "windows.specs.title";
        // Spec detail title is dynamic (spec code); the caller passes a resolved
        // literal (or this generic key as a fallback) rather than an i18n lookup.
        public const string SpecDetail = "windows.spec_detail.title";

        // P2 showcard-migration — Traceability list + per-WO detail.
        public const string Traceability = "windows.traceability.title";
        // Detail title is dynamic ("WO {WoNo}"); the caller passes the resolved
        // literal, this key is a generic fallback.
        public const string TraceDetail = "windows.trace_detail.title";

        // W5 showcard-migration — IQC Materials inspection window. Title is
        // dynamic (the saved ticket's ReceiptNo, or the "new ticket" phrase for
        // a create window); this key is the generic fallback the caller uses.
        public const string IqcInspection = "windows.iqc_inspection.title";
    }

    /// <summary>RBAC roles for QcLibrary (mirrors its page <c>[Authorize]</c>).
    /// The other three are any-auth (null roles).</summary>
    public static readonly string[] QcLibraryRoles = { "Admin", "Supervisor", "Engineer", "QC" };

    /// <summary>RBAC roles for the 4 NPI grids (mirror their page
    /// <c>[Authorize(Roles="Admin,Supervisor,Engineer,QC")]</c>). Home + Semi +
    /// the 3 QMS modules are any-auth (null roles) so they carry no array.</summary>
    public static readonly string[] NpiRoles = { "Admin", "Supervisor", "Engineer", "QC" };

    /// <summary>RBAC roles for the Spec list + detail windows (mirror
    /// <c>[Authorize(Roles = "Admin,Supervisor,Engineer")]</c> on both pages).</summary>
    public static readonly string[] SpecRoles = { "Admin", "Supervisor", "Engineer" };

    /// <summary>RBAC roles for the QMS Inspection Queue windows (mirror the
    /// NavMenu <c>&lt;AuthorizeView Roles="Admin,Supervisor,QC"&gt;</c> that
    /// wraps the QMS group; the server still authorises each route).</summary>
    public static readonly string[] QmsQueueRoles = { "Admin", "Supervisor", "QC" };

    /// <summary>RBAC roles for the Traceability list + per-WO detail windows
    /// (mirror the page <c>[Authorize(Roles = "Admin,Supervisor,QC")]</c> + the
    /// NavMenu <c>&lt;AuthorizeView&gt;</c> that wraps the QMS group; the server
    /// still authorises each route).</summary>
    public static readonly string[] TraceabilityRoles = { "Admin", "Supervisor", "QC" };

    /// <summary>Static parameter set for the FQC stage queue window:
    /// <c>{ ["Mode"] = "fqc" }</c>. The hub (/qms) carries no params.</summary>
    public static readonly IReadOnlyDictionary<string, object> QmsQueueFqcParameters =
        new Dictionary<string, object> { ["Mode"] = "fqc" };
}
