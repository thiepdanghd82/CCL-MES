using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Hardware;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// P10.3 W4 — background ping that POSTs a heartbeat to
/// <c>/v2/devices/{id}/heartbeat</c> every 60 seconds while the user
/// is logged in. The server uses these to render an admin "kiosk
/// health" view + emit DEVICE_RECONNECT audit rows when a station
/// returns after a >5 min gap. Stops on cancellation (app teardown).
///
/// Guard rails:
///   - Suspends when there is no logged-in user (heartbeat is a JWT
///     gated endpoint; we'd just get 401s).
///   - Catches all exceptions per tick so a transient network failure
///     doesn't kill the background loop — we just skip and try again.
///   - Uses <see cref="PeriodicTimer"/> for the cadence so a slow API
///     response doesn't drift the schedule.
/// </summary>
internal sealed class DeviceHeartbeatHostedService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    private readonly IServiceProvider _sp;
    private readonly ILogger<DeviceHeartbeatHostedService> _log;

    public DeviceHeartbeatHostedService(IServiceProvider sp, ILogger<DeviceHeartbeatHostedService> log)
    {
        _sp = sp;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "[heartbeat] tick failed — will retry on next interval.");
            }
        } while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        // Each tick takes a fresh scope so transient services (HttpClient)
        // dispose cleanly. The singletons (IAuthSession, IDeviceModeService)
        // are unaffected.
        await using var scope = _sp.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<IAuthSession>();
        if (session.CurrentUser?.Identity?.IsAuthenticated != true) return;

        var device = scope.ServiceProvider.GetRequiredService<IDeviceModeService>();
        var api = scope.ServiceProvider.GetRequiredService<ICclApiClient>();

        var req = new HeartbeatRequest
        {
            AppVersion = AppInfo.VersionString,
            Mode = device.CurrentMode == DeviceMode.Kiosk ? "kiosk" : "interactive",
            Platform = ResolvePlatform(),
        };
        await api.HeartbeatAsync(req, ct);
    }

    private static string ResolvePlatform()
    {
#if MACCATALYST
        return "MacCatalyst";
#elif IOS
        return "iOS";
#elif WINDOWS
        return "Windows";
#elif ANDROID
        return "Android";
#else
        return "Unknown";
#endif
    }
}
