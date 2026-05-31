namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 5 — carries the ccl_mes_auth cookie value into the Blazor Server
/// circuit so server-side HubConnections (Dashboard, WorkOrders) can forward
/// it on the SignalR negotiate request. See docs/PHASE5-STEP2-PLAN.md.
///
/// Captured once in _Host.cshtml at first render (HttpContext alive there);
/// read later when each page builds its HubConnection.
///
/// Scoped lifetime = one instance per Blazor circuit. The circuit teardown
/// on logout/forceLoad clears it, so a stale cookie never survives a
/// re-login in the same tab.
/// </summary>
public class HubCookieAccessor
{
    public string? AuthCookie { get; set; }
}
