namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 9 hub-reconnect — 4-state UX classification for
/// <see cref="Shared.HubSessionBanner"/>. Locked-in shape per
/// <c>docs/PHASE9-HUB-RECONNECT-PLAN.md</c> §4 Q1:
/// <list type="bullet">
///   <item><see cref="Connected"/>      — banner hidden (default).</item>
///   <item><see cref="Reconnecting"/>   — banner "Đang kết nối lại…" (yellow, non-blocking).</item>
///   <item><see cref="Reconnected"/>    — toast confirm 3s auto-dismiss (info/blue).</item>
///   <item><see cref="SessionExpired"/> — sticky red banner + Reload button.
///         Triggered when <see cref="Microsoft.AspNetCore.SignalR.Client.HubConnection.Closed"/>
///         fires after the automatic-reconnect budget is exhausted, OR
///         immediately on a 401 negotiate handshake. Operator MUST
///         reload — the page-level <c>FallbackPolicy</c> auth gate
///         routes through <c>/login</c> when the cookie is dead.</item>
/// </list>
/// </summary>
public enum HubSessionState
{
    Connected,
    Reconnecting,
    Reconnected,
    SessionExpired,
}
