using Microsoft.AspNetCore.SignalR;

namespace CCL.MES.Web.Hubs;

/// <summary>
/// Hub SignalR phát sự kiện shopfloor (WO đổi bước, QC, OEE) tới mọi client
/// đang mở Dashboard / Work Orders để cập nhật realtime.
/// </summary>
public class ShopfloorHub : Hub
{
}

/// <summary>
/// Bộ phát sự kiện dùng được từ tầng Web (inject vào trang Blazor).
/// Gọi NotifyChangedAsync() sau mỗi thay đổi để các client khác reload.
/// </summary>
public class ShopfloorNotifier
{
    private readonly IHubContext<ShopfloorHub> _hub;
    public ShopfloorNotifier(IHubContext<ShopfloorHub> hub) => _hub = hub;

    public Task NotifyChangedAsync(string reason = "changed") =>
        _hub.Clients.All.SendAsync("shopfloorChanged", reason);
}
