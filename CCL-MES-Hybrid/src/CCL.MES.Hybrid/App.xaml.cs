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

        // P10.6a hotfix — per-service try/catch INSIDE the foreach so
        // one bad hosted service can't skip the others. The previous
        // shape wrapped the entire foreach so service N's failure
        // prevented service N+1 from starting.
        //
        // BackgroundService semantics: StartAsync stores
        // _executeTask = ExecuteAsync(...) and returns immediately, so
        // the long-running work lives in a fire-and-forget Task. When
        // ExecuteAsync throws, the TaskScheduler.UnobservedTaskException
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
