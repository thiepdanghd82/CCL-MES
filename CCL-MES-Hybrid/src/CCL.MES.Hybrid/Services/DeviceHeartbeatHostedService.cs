using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Shared.Devices;
using CCL.MES.Shared.Hardware;
using Microsoft.Extensions.DependencyInjection;
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
/// Guard rails (P10.5g hardening — Henry's "Blazor renderer dies
/// after N seconds" incident):
///   - Suspends when there is no logged-in user (heartbeat is a JWT
///     gated endpoint; we'd just get 401s).
///   - Catches all exceptions per tick so a transient network failure
///     doesn't kill the background loop — we just skip and try again.
///   - The OUTER loop is ALSO wrapped so a throw from <c>WaitForNextTickAsync</c>,
///     <c>PeriodicTimer.Dispose</c>, or any DI resolution failure CAN
///     NOT escape this <see cref="BackgroundService"/>. An unobserved
///     <see cref="Task"/> exception here used to be caught by
///     <see cref="GlobalErrorLogger"/> only after the renderer had
///     already wound down — the renderer recovery path was the
///     symptom Henry saw. The double-wrap is observation-cheap and
///     keeps the BackgroundService alive for the next 60-second tick.
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
        // P10.5g outer guard — even a throw escaping the inner loop
        // (PeriodicTimer.Dispose, scope.CreateAsyncScope failure, DI
        // graph corruption) lands HERE instead of becoming an
        // unobserved task exception that takes the WebView with it.
        try
        {
            await RunLoopAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — host cancellation propagated.
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[heartbeat] outer loop crashed — service will not retick.");
            GlobalErrorLogger.Log("DeviceHeartbeatHostedService",
                "outer loop crashed — service will not retick until app restart.", ex);
        }
    }

    private async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        // Startup delay so the boot path isn't competing with the
        // first-render work. OCE flows out via the outer try.
        try { await Task.Delay(StartupDelay, stoppingToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[heartbeat] startup delay threw — proceeding into loop anyway.");
            GlobalErrorLogger.Log("DeviceHeartbeatHostedService.startupDelay",
                "Task.Delay threw a non-OCE exception", ex);
        }

        PeriodicTimer? timer = null;
        try
        {
            timer = new PeriodicTimer(Interval);
            do
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Tick failures are operationally normal (server down,
                    // 401 during the auth-state-change race). LogDebug
                    // kept for high-volume cases; GlobalErrorLogger.Log
                    // surfaces in the rolling file so an operator-side
                    // incident leaves a trail.
                    _log.LogDebug(ex, "[heartbeat] tick failed — will retry on next interval.");
                    GlobalErrorLogger.Log("DeviceHeartbeatHostedService.tick",
                        "tick failed; loop continues", ex);
                }
            } while (!stoppingToken.IsCancellationRequested
                     && await SafeWaitNextTickAsync(timer, stoppingToken));
        }
        finally
        {
            timer?.Dispose();
        }
    }

    /// <summary>Wrap <see cref="PeriodicTimer.WaitForNextTickAsync"/> so
    /// a transient throw (extremely rare but documented in dotnet#71860)
    /// is contained — we just exit the loop cleanly instead of letting
    /// the exception propagate up through the BackgroundService
    /// surface.</summary>
    private async Task<bool> SafeWaitNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            GlobalErrorLogger.Log("DeviceHeartbeatHostedService.WaitForNextTickAsync",
                "PeriodicTimer.WaitForNextTickAsync threw", ex);
            return false;
        }
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
