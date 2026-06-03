namespace CCL.MES.Hybrid.Client.Connectivity;

/// <summary>
/// Network-reachability signal consumed by the UI to render the
/// "connection lost" banner without ever crashing. The MAUI shell wraps
/// <c>Microsoft.Maui.Networking.Connectivity</c>; tests + web hosts can
/// substitute a static "always online" implementation.
///
/// Q4 lock (2026-06-03): full offline writes land in P10.4. The
/// connectivity signal here is for UX only — operators see a banner +
/// reads from the server fail gracefully — there is no offline-write
/// queue in Phase 10.
/// </summary>
public interface IConnectivityMonitor
{
    /// <summary>True when the device reports network access.</summary>
    bool IsConnected { get; }

    /// <summary>Fires when <see cref="IsConnected"/> flips.</summary>
    event Action<bool>? ConnectivityChanged;
}

/// <summary>
/// Trivial implementation that pretends the network is always up — used
/// by xUnit tests + as a fallback when the MAUI Connectivity API is
/// unavailable (e.g. headless server-side rendering).
/// </summary>
public sealed class AlwaysOnlineConnectivityMonitor : IConnectivityMonitor
{
    public bool IsConnected => true;
    public event Action<bool>? ConnectivityChanged;
    public void RaiseForTests(bool isConnected) => ConnectivityChanged?.Invoke(isConnected);
}
