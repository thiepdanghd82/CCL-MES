using CCL.MES.Hybrid.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Hybrid;

public partial class App : Application
{
    public App(IServiceProvider services)
    {
        InitializeComponent();
        MainPage = new MainPage();

        // P10.5g hotfix — hosted-service kick-off MUST NOT bubble up:
        // a throw here ends the long-running task in the void path
        // and Mac Catalyst's runtime may silently take the BlazorWebView
        // renderer with it (the "click does nothing after N seconds"
        // symptom). The inner BackgroundService now has its own outer
        // guard (DeviceHeartbeatHostedService.RunLoopAsync); this
        // wrapper handles the host-startup-time failure mode (DI
        // resolution, ServiceProvider disposal during teardown).
        //
        // Note on BackgroundService semantics: StartAsync stores
        // _executeTask = ExecuteAsync(...) and returns immediately, so
        // the long-running work lives in a fire-and-forget Task. Even
        // when ExecuteAsync throws, the TaskScheduler.UnobservedTaskException
        // path now lands in GlobalErrorLogger (armed in MauiProgram)
        // so an unobserved throw is observable + non-fatal.
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var hs in services.GetServices<IHostedService>())
                {
                    try
                    {
                        await hs.StartAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        // One service failing must NOT skip the others.
                        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("HostedServiceStartup");
                        logger?.LogError(ex, "Hosted service {Name} failed to start.", hs.GetType().Name);
                        GlobalErrorLogger.Log("HostedServiceStartup",
                            $"{hs.GetType().Name}.StartAsync threw", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetService<ILoggerFactory>()?.CreateLogger("HostedServiceStartup");
                logger?.LogError(ex, "Hosted service enumeration failed.");
                GlobalErrorLogger.Log("HostedServiceStartup",
                    "GetServices<IHostedService>() threw", ex);
            }
        });
    }
}
