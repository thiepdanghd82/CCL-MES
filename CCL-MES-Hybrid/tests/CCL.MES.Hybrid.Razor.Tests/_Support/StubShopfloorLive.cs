using CCL.MES.Hybrid.Client.Realtime;

namespace CCL.MES.Hybrid.Razor.Tests._Support;

/// <summary>Test double for the real-time hub — lets a test flip Live/Offline
/// and raise a "change" signal without a real SignalR connection.</summary>
public sealed class StubShopfloorLive : IShopfloorLiveService
{
    private bool _connected;

    public bool IsConnected => _connected;
    public event Action<string>? Changed;
    public event Action? StateChanged;

    public Task EnsureStartedAsync(CancellationToken ct = default) => Task.CompletedTask;

    public void SetConnected(bool on) { _connected = on; StateChanged?.Invoke(); }
    public void RaiseChanged(string reason = "trace_updated:WO") => Changed?.Invoke(reason);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
