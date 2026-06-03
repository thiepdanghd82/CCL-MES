using Microsoft.AspNetCore.SignalR.Client;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 9 hub-reconnect — shared <see cref="HubConnection"/> wiring
/// for the 4-state UX banner. Reuses
/// <see cref="HubConnectionBuilder.WithAutomaticReconnect()"/> default
/// retry budget <c>[0, 2, 10, 30]s</c>; when the budget runs out,
/// <see cref="HubConnection.Closed"/> fires with the last exception
/// and we transition the UI to
/// <see cref="HubSessionState.SessionExpired"/>.
///
/// <para>
/// <b>UX state map</b> (locked in per
/// <c>docs/PHASE9-HUB-RECONNECT-PLAN.md</c> §2 Option A):
/// <list type="bullet">
///   <item><c>Reconnecting</c> event → <see cref="HubSessionState.Reconnecting"/></item>
///   <item><c>Reconnected</c>  event → <see cref="HubSessionState.Reconnected"/>
///         (banner consumer auto-dismisses after a short toast window)</item>
///   <item><c>Closed</c>       event → <see cref="HubSessionState.SessionExpired"/>
///         — final state. The 4-state rollup intentionally does NOT
///         distinguish 401-from-cookie vs network-blip-exhausted-budget;
///         both resolve through a <c>NavigateTo(forceLoad: true)</c>
///         reload which will route through <c>/login</c> when the
///         cookie is actually dead and back to the same page otherwise.
///         Per Henry's Q3 + Q4 (PR #67 plan).</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Marshaling</b>: <see cref="HubConnection.Reconnecting"/> +
/// <see cref="HubConnection.Reconnected"/> +
/// <see cref="HubConnection.Closed"/> fire on a SignalR worker thread.
/// Blazor Server UI updates MUST run on the circuit's renderer thread,
/// so the callback wraps the state update in
/// <see cref="ComponentBase.InvokeAsync"/> (caller supplies the
/// dispatcher delegate to keep this helper Blazor-thin).
/// </para>
///
/// <para>
/// <b>Constraint</b>: This helper is client-side ONLY. It does NOT
/// touch <c>ShopfloorHub</c> server logic, <c>ShopfloorNotifier</c>
/// broadcast contract, cookie auth config, or the state machine. The
/// only change to existing pages is wiring the 3 event handlers; the
/// existing <c>WithAutomaticReconnect()</c> + cookie forwarding
/// remain unchanged.
/// </para>
/// </summary>
public static class HubConnectionExtensions
{
    /// <summary>
    /// Wire the 3 SignalR connection-lifecycle events so the
    /// supplied banner callback receives a single normalised
    /// <see cref="HubSessionState"/> stream.
    /// </summary>
    /// <param name="hub">The active <see cref="HubConnection"/>.</param>
    /// <param name="onState">
    /// Callback invoked from the SignalR worker thread. Callers
    /// typically wrap their UI mutation in
    /// <c>InvokeAsync(StateHasChanged)</c> inside this delegate so
    /// Blazor renders on the right thread.
    /// </param>
    public static void WireSessionHandlers(
        this HubConnection hub,
        Func<HubSessionState, Task> onState)
    {
        if (hub is null) throw new ArgumentNullException(nameof(hub));
        if (onState is null) throw new ArgumentNullException(nameof(onState));

        hub.Reconnecting += _ => onState(HubSessionState.Reconnecting);
        hub.Reconnected  += _ => onState(HubSessionState.Reconnected);
        hub.Closed       += _ => onState(HubSessionState.SessionExpired);
    }
}
