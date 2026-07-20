using CCL.MES.Hybrid.Client.Auth;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Options;

namespace CCL.MES.Hybrid.Client.Realtime;

/// <summary>
/// Shared, app-wide SignalR client for the shopfloor hub (/hubs/shopfloor).
/// ONE connection reused by every page (not one-per-page). Notify-then-pull:
/// the server only says "something changed" (a reason string) — subscribers
/// re-pull their own data. Auto-reconnects; JWT is supplied per (re)connect
/// from <see cref="ITokenStore"/> so a rotated token is picked up on the next
/// reconnect. When the socket is down, pages fall back to light polling and
/// flip a Live/Offline badge off <see cref="IsConnected"/>.
/// </summary>
public interface IShopfloorLiveService : IAsyncDisposable
{
    bool IsConnected { get; }
    /// <summary>Fires with the server reason string (e.g. "trace_updated:WO1").</summary>
    event Action<string>? Changed;
    /// <summary>Fires whenever the connection state flips (badge + fallback).</summary>
    event Action? StateChanged;
    Task EnsureStartedAsync(CancellationToken ct = default);
}

public sealed class ShopfloorLiveService : IShopfloorLiveService
{
    private readonly ITokenStore _tokens;
    private readonly ApiClientOptions _opts;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private HubConnection? _hub;

    public ShopfloorLiveService(ITokenStore tokens, IOptions<ApiClientOptions> opts)
    {
        _tokens = tokens; _opts = opts.Value;
    }

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;

    public event Action<string>? Changed;
    public event Action? StateChanged;

    public async Task EnsureStartedAsync(CancellationToken ct = default)
    {
        if (_hub is not null)
        {
            if (_hub.State == HubConnectionState.Disconnected)
            {
                try { await _hub.StartAsync(ct); } catch { /* stay offline → page polls */ }
                StateChanged?.Invoke();
            }
            return;
        }

        await _startLock.WaitAsync(ct);
        try
        {
            if (_hub is not null) return;

            var url = _opts.BaseUrl.TrimEnd('/') + "/hubs/shopfloor";
            var hub = new HubConnectionBuilder()
                .WithUrl(url, o =>
                {
                    // Re-read the token on every (re)connect → rotation-safe.
                    o.AccessTokenProvider = async () => await _tokens.GetAccessTokenAsync();
                })
                .WithAutomaticReconnect()
                .Build();

            hub.On<string>("shopfloorChanged", reason => Changed?.Invoke(reason));
            hub.Reconnecting += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
            hub.Reconnected += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };
            hub.Closed += _ => { StateChanged?.Invoke(); return Task.CompletedTask; };

            _hub = hub;
            try { await hub.StartAsync(ct); } catch { /* offline → fallback polling */ }
            StateChanged?.Invoke();
        }
        finally { _startLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null) await _hub.DisposeAsync();
        _startLock.Dispose();
    }
}
