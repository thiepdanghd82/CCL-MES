using CCL.MES.Hybrid.Client.Connectivity;
using Microsoft.Maui.Networking;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// <see cref="IConnectivityMonitor"/> on top of MAUI's
/// <see cref="Connectivity"/> API. The MAUI implementation marshals
/// platform-specific reachability events (NWPathMonitor on Mac, the
/// connection-cost notifier on Windows) onto a single managed event.
/// Q4 lock: this drives the UX banner only — there is no offline-write
/// queue in Phase 10.
/// </summary>
public sealed class MauiConnectivityMonitor : IConnectivityMonitor, IDisposable
{
    public bool IsConnected { get; private set; }
    public event Action<bool>? ConnectivityChanged;

    public MauiConnectivityMonitor()
    {
        IsConnected = Connectivity.Current.NetworkAccess == NetworkAccess.Internet;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
    }

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var nowConnected = e.NetworkAccess == NetworkAccess.Internet;
        if (nowConnected == IsConnected) return;
        IsConnected = nowConnected;
        ConnectivityChanged?.Invoke(nowConnected);
    }

    public void Dispose()
    {
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
    }
}
