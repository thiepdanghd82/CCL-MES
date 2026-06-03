using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace CCL.MES.Api.Hubs;

/// <summary>
/// V2 SignalR hub serving the new MAUI Hybrid client. Mirrors the legacy
/// <c>CCL.MES.Web.Hubs.ShopfloorHub</c> shape (empty class, broadcast via a
/// notifier) but lives in the Api project and authenticates over JWT
/// Bearer rather than cookie. Clients connect to
/// <c>wss://&lt;host&gt;/hubs/shopfloor?access_token=&lt;jwt&gt;</c>
/// (query-string token because browsers won't set custom headers on the
/// initial WebSocket negotiation).
///
/// Push semantics identical to legacy: every mutation emits a single
/// <c>shopfloorChanged</c> event with a short reason string ("wo_advance",
/// "qc_pass", …). Clients respond by pulling the affected resource.
/// </summary>
[Authorize]
public sealed class ShopfloorHubV2 : Hub
{
    // Hub kept intentionally minimal — matches the legacy ShopfloorHub
    // contract. Add inbound client→server methods here when an MAUI use
    // case (e.g. presence ping) requires one.
}

/// <summary>
/// Server-side broadcast helper. Inject into endpoints that mutate
/// shopfloor state — call <see cref="NotifyChangedAsync"/> after the
/// SaveChangesAsync to fan out a single push event to connected clients.
/// </summary>
public sealed class ShopfloorNotifierV2
{
    private readonly IHubContext<ShopfloorHubV2> _hub;
    public ShopfloorNotifierV2(IHubContext<ShopfloorHubV2> hub) => _hub = hub;

    public Task NotifyChangedAsync(string reason = "changed") =>
        _hub.Clients.All.SendAsync("shopfloorChanged", reason);
}
